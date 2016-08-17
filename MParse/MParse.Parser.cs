using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSFunc.Types;
using MParse.Lexer;

using TokenList = System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>;
using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>>;
using AST = MParse.Parser.Tree<MParse.Parser.Term>;

namespace MParse.Parser
{
    public static class Parser
    {
        public static Error<AST, ParseError> DoParse(NonTerminal Start, TokenList input, bool showIntermediateResults = false)
        {
            ParseState parsed = Start(ParseState.Return(Tuple.Create(TokenList.Empty, input, new AST())));
            parsed = parsed.Bind(state => state.Item2.Count == 0
                                          ? parsed
                                          : ParseState.Throw(new ParseError(ParseError.ExpectedValue.EOF(), ParseError.GotValue.Token(state.Item2[0]), state.Item2[0].Location, state)));
            return parsed.Map(value => value.Item3);
        }

        // Parsing methods

        public static ParseState Parse(this ParseState prev, int token)
        {
            if (prev.State == ErrorState.Result)
            {
                try
                {
                    return (from state in prev
                            from result in (state.Item2[0].Type == token)
                                           ? ParseState.Result(Tuple.Create(
                                                               state.Item1.Add(state.Item2[0]),
                                                               state.Item2.Skip(1).ToImmutableList(),
                                                               new Tree<Term>(state.Item3.Value, state.Item3.Children.Add(new Tree<Term>(Term.Terminal(state.Item2[0]))))))
                                           : ParseState.Throw(new ParseError(ParseError.ExpectedValue.Token(token), ParseError.GotValue.Token(state.Item2[0]), state.Item2[0].Location, state))
                            select result);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return prev.Bind(state => ParseState.Throw(new ParseError(ParseError.ExpectedValue.Token(token), ParseError.GotValue.EOF(), state.Item1[state.Item1.Count - 1].Location, state)));
                }
            }
            else return prev;
        }

        public static ParseState Parse(this ParseState prev, Func<ParseState, ParseState> group)
        {
            if (prev.State == ErrorState.Result) return prev.Bind(oldstate => group(ParseState.Result(Tuple.Create(oldstate.Item1, oldstate.Item2, new AST())))
                                                                              .Match(Result: state => ParseState.Result(Tuple.Create(state.Item1,
                                                                                                                                     state.Item2,
                                                                                                                                     new AST(oldstate.Item3.Value, oldstate.Item3.Children.Add(state.Item3)))),
                                                                                     Throw: terr => ParseState.Throw(terr)));
            else return prev;
        }

        public static ParseState Option(this ParseState prev, params Tuple<Func<ParseState, ParseState>, string>[] options)
        {
            if (prev.State == ErrorState.Result)
            {
                ParseState result;
                Func<ParseState, ParseState>[] _options = options.Select(o => o.Item1).ToArray();
                foreach (Func<ParseState, ParseState> option in _options)
                {
                    try
                    {
                        result = prev.Parse(option);
                    }
                    catch (IndexOutOfRangeException)
                    {
                        continue;
                    }
                    if (result.State == ErrorState.Result) return result;
                    else continue;
                }
                return prev.Bind(state =>
                    state.Item2.Count == 0
                    ? ParseState.Throw(new ParseError(ParseError.ExpectedValue.Option(options.Select(o => o.Item2).ToArray()), ParseError.GotValue.EOF(), state.Item1[state.Item1.Count - 1].Location, state))
                    : ParseState.Throw(new ParseError(ParseError.ExpectedValue.Option(options.Select(o => o.Item2).ToArray()), ParseError.GotValue.None(), state.Item2[0].Location, state)));
            }
            else return prev;
        }

        public static Tuple<Func<ParseState, ParseState>, string> Option(Func<ParseState, ParseState> ps, string description) => Tuple.Create(ps, description);

        public static ParseState Loop(this ParseState prev, NonTerminal nt)
        {
            if (prev.State == ErrorState.Result)
            {
                ParseState parsed = prev;
                while (true)
                {
                    ParseState _parsed = parsed.Parse(nt);
                    if (_parsed.State == ErrorState.Throw) return parsed;
                    else parsed = _parsed;
                }
            }
            else return prev;
        }

        public static ParseState Rule(this ParseState prev, int rulenum) => prev.Map(state => Tuple.Create(state.Item1, state.Item2, new AST(Term.NonTerminal(rulenum), state.Item3.Children)));

        public static NonTerminal Rule(this NonTerminal nt, int rulenum) => prev => prev.Parse(nt).Rule(rulenum);

        // Utility methods

        public static ParseState epsilon(ParseState text) => text.Rule(-1);

        public static Token Token(int type, string value, ILocation location) => new Token(type, value, location);

        public static Maybe<Token[]> Lookahead(this ParseState p, int n) => p.Match(Result: state => (state.Item2.Count < n) ? Maybe<Token[]>.Nothing()
                                                                                                                       : Maybe<Token[]>.Just(state.Item2.Take(n).ToArray()),
                                                                                    Throw: terr => Maybe<Token[]>.Nothing());

        public static NonTerminal ParseToks(int rulenum, params int[] toks) => (ParseState prev) =>
        {
            if (prev.State == ErrorState.Result)
            {
                ParseState parsed = prev;
                foreach (int tok in toks)
                {
                    parsed = parsed.Parse(tok);
                }
                return parsed.Rule(rulenum);
            }
            else return prev;
        };
    }

    #region Term
    // Term = Terminal Token | NonTerminal int | Loop int | EndLoop
    public class Term
    {
        private class TerminalImpl
        {
            public Token Value1 { get; set; } = default(Token);
            public TerminalImpl(Token value1)
            {
                Value1 = value1;
            }
        }
        private class NonTerminalImpl
        {
            public int Value1 { get; set; } = default(int);
            public NonTerminalImpl(int value1)
            {
                Value1 = value1;
            }
        }
        private class LoopImpl
        {
            public int Value1 { get; set; } = default(int);
            public LoopImpl(int value1)
            {
                Value1 = value1;
            }
        }
        private class EndLoopImpl
        {
            public EndLoopImpl()
            {
            }
        }
        public TermState State { get; set; }
        private TerminalImpl TerminalField;
        private TerminalImpl TerminalValue { get { return TerminalField; } set { TerminalField = value; NonTerminalField = null; LoopField = null; EndLoopField = null; State = TermState.Terminal; } }
        private NonTerminalImpl NonTerminalField;
        private NonTerminalImpl NonTerminalValue { get { return NonTerminalField; } set { NonTerminalField = value; TerminalField = null; LoopField = null; EndLoopField = null; State = TermState.NonTerminal; } }
        private LoopImpl LoopField;
        private LoopImpl LoopValue { get { return LoopField; } set { LoopField = value; TerminalField = null; NonTerminalField = null; EndLoopField = null; State = TermState.Loop; } }
        private EndLoopImpl EndLoopField;
        private EndLoopImpl EndLoopValue { get { return EndLoopField; } set { EndLoopField = value; TerminalField = null; NonTerminalField = null; LoopField = null; State = TermState.EndLoop; } }
        private Term() { }
        public static Term Terminal(Token value1)
        {
            Term result = new Term();
            result.TerminalValue = new TerminalImpl(value1);
            return result;
        }
        public static Term NonTerminal(int value1)
        {
            Term result = new Term();
            result.NonTerminalValue = new NonTerminalImpl(value1);
            return result;
        }
        public static Term Loop(int value1)
        {
            Term result = new Term();
            result.LoopValue = new LoopImpl(value1);
            return result;
        }
        public static Term EndLoop()
        {
            Term result = new Term();
            result.EndLoopValue = new EndLoopImpl();
            return result;
        }
        public T1 Match<T1>(Func<Token, T1> Terminal, Func<int, T1> NonTerminal, Func<int, T1> Loop, Func<T1> EndLoop)
        {
            switch (State)
            {
                case TermState.Terminal: return Terminal(TerminalValue.Value1);
                case TermState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
                case TermState.Loop: return Loop(LoopValue.Value1);
                case TermState.EndLoop: return EndLoop();
            }
            return default(T1);
        }
        public override string ToString()
        {
            return this.Match(Terminal: tok => "Terminal " + tok.ToString(), NonTerminal: nt => "Nonterminal " + nt, Loop: l => "Loop " + l, EndLoop: () => "EndLoop");
        }
    }
    public enum TermState
    {
        Terminal, NonTerminal, Loop, EndLoop
    }
    #endregion

    public class Tree<T>
    {
        public T Value { get; }
        public ImmutableList<Tree<T>> Children { get; }
        public bool IsLeaf => Children.Count == 0;

        public Tree() : this(default(T)) { }

        public Tree(T t) : this(t, ImmutableList<Tree<T>>.Empty) { }

        public Tree(T t, ImmutableList<Tree<T>> children)
        {
            Value = t;
            Children = children;
        }

        public Tree<T1> Map<T1>(Func<T, T1> f) => new Tree<T1>(f(this.Value), this.Children.Select(c => c.Map(f)).ToImmutableList());

        public Tree<T1> MapTree<T1>(Func<Tree<T>, T1> f) => new Tree<T1>(f(this), this.Children.Select(c => c.MapTree(f)).ToImmutableList());

        public Tree<T> Navigate(ImmutableList<int> directions) => directions.Count == 0
                                                                  ? this
                                                                  : this.Children[directions[0]].Navigate(directions.Skip(1).ToImmutableList());

        private Tree<Tuple<T, ImmutableList<int>>> Direct(ImmutableList<int> acc) =>
            new Tree<Tuple<T, ImmutableList<int>>>(Tuple.Create(this.Value, acc), this.Children.Select((c, i) => c.Direct(acc.Add(i))).ToImmutableList());

        private List<T> Flatten() => new List<T>{this.Value}.Concat(this.Children.SelectMany(c => c.Flatten())).ToList();

        public Maybe<ImmutableList<int>> Rightmost(Func<Tree<T>, bool> predicate) =>
            this.MapTree(predicate)
                .Direct(ImmutableList<int>.Empty)
                .Flatten()
                .Where(v => v.Item1)
                .Aggregate(Maybe<ImmutableList<int>>.Nothing(), (acc, v) => Maybe<ImmutableList<int>>.Just(v.Item2));

        private static void ClearLine() // With thanks to SomeNameNoFake from http://stackoverflow.com/questions/8946808/can-console-clear-be-used-to-only-clear-a-line-instead-of-whole-console
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, Console.CursorTop - (Console.WindowWidth >= Console.BufferWidth ? 1 : 0));
        }

        public void PrintPretty(string indent = "", bool last = true) // With thanks to Will from http://stackoverflow.com/questions/1649027/how-do-i-print-out-a-tree-structure
        {
            ClearLine(); // In case there is already something on this line
            Console.Write(indent);
            if (last)
            {
                Console.Write(@"└─");
                indent += "  ";
            }
            else
            {
                Console.Write("├─");
                indent += "│ ";
            }
            Console.WriteLine(this.Value.ToString());
            for (int i = 0; i < this.Children.Count; i++)
                this.Children[i].PrintPretty(indent, i == this.Children.Count - 1);

        }
    }

    public class ParseError : TokenError
    {
        public class ExpectedValue
        {
            // ExpectedValue = EOF | Token int | Option string[]
            public ExpectedValueState State { get; set; }
            private Unit EOFField;
            private Unit EOFValue { get { return EOFField; } set { EOFField = value; TokenField = 0; OptionField = null; State = ExpectedValueState.EOF; } }
            private int TokenField;
            private int TokenValue { get { return TokenField; } set { TokenField = value; EOFField = null; OptionField = null; State = ExpectedValueState.Token; } }
            private string[] OptionField;
            private string[] OptionValue { get { return OptionField; } set { OptionField = value; EOFField = null; TokenField = 0; State = ExpectedValueState.Option; } }
            private ExpectedValue() { }
            public static ExpectedValue EOF()
            {
                ExpectedValue result = new ExpectedValue();
                result.EOFValue = Unit.Nil;
                return result;
            }
            public static ExpectedValue Token(int value1)
            {
                ExpectedValue result = new ExpectedValue();
                result.TokenValue = value1;
                return result;
            }
            public static ExpectedValue Option(string[] value1)
            {
                ExpectedValue result = new ExpectedValue();
                result.OptionValue = value1;
                return result;
            }
            public T1 Match<T1>(Func<T1> EOF, Func<int, T1> Token, Func<string[], T1> Option)
            {
                switch (State)
                {
                    case ExpectedValueState.EOF: return EOF();
                    case ExpectedValueState.Token: return Token(TokenValue);
                    case ExpectedValueState.Option: return Option(OptionValue);
                }
                return default(T1);
            }
        }
        public enum ExpectedValueState
        {
            EOF, Token, Option
        }
        public class GotValue
        {
            // GotValue = EOF | Token Token | None
            public GotValueState State { get; set; }
            private Unit EOFField;
            private Unit EOFValue { get { return EOFField; } set { EOFField = value; TokenField = new Token(); NoneField = null; State = GotValueState.EOF; } }
            private Token TokenField;
            private Token TokenValue { get { return TokenField; } set { TokenField = value; EOFField = Unit.Nil; NoneField = null; State = GotValueState.Token; } }
            private Unit NoneField;
            private Unit NoneValue { get { return NoneField; } set { NoneField = value; TokenField = new Token(); EOFField = null; State = GotValueState.EOF; } }
            private GotValue() { }
            public static GotValue EOF()
            {
                GotValue result = new GotValue();
                result.EOFValue = Unit.Nil;
                return result;
            }
            public static GotValue Token(Token value1)
            {
                GotValue result = new GotValue();
                result.TokenValue = value1;
                return result;
            }
            public static GotValue None()
            {
                GotValue result = new GotValue();
                result.NoneValue = Unit.Nil;
                return result;
            }
            public T1 Match<T1>(Func<T1> EOF, Func<Token, T1> Token, Func<T1> None)
            {
                switch (State)
                {
                    case GotValueState.EOF: return EOF();
                    case GotValueState.Token: return Token(TokenValue);
                    case GotValueState.None: return None();
                }
                return default(T1);
            }
        }
        public enum GotValueState
        {
            EOF, Token, None
        }
        public ExpectedValue Expected { get; }
        public GotValue Got { get; }
        public Tuple<TokenList, TokenList, AST> Previous { get; set; }
        public ParseError(ExpectedValue expected, GotValue got, ILocation location, Tuple<TokenList, TokenList, AST> previous) : base(location)
        {
            Expected = expected;
            Got = got;
            Previous = previous;
        }
        public string ToString(Dictionary<int, string> tokenMap, bool putPositionAtFront = true)
        {
            string value = "";
            if (putPositionAtFront) value += Location.ToString() + " ";
            value += "Error: Expected ";
            value += Expected.Match(EOF: () => "EOF",
                                    Token: t => tokenMap[t],
                                    Option: options =>
                                    {
                                        string description = "";
                                        for (int i = 0; i < options.Count(); i++)
                                        {
                                            if (i != options.Count() - 1) description += options[i] + ", ";
                                            else description += "or " + options[i];
                                        }
                                        return description;
                                    });
            value += Got.Match(EOF: () => ", but got EOF",
                               Token: t => ", but got " + tokenMap[t.Type],
                               None: () => ""
                              );
            if (!putPositionAtFront) value += " at " + Location.ToString();
            return value;
        }
    }
}
