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
                                          : ParseState.Throw(new ParseError(new ParseError.ExpectedValue.EOF(), new ParseError.GotValue.Token(state.Item2[0]), state.Item2[0].Location, state)));
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
                                                               new Tree<Term>(state.Item3.Value, state.Item3.Children.Add(new Tree<Term>(new Term.Terminal(state.Item2[0]))))))
                                           : ParseState.Throw(new ParseError(new ParseError.ExpectedValue.Token(token), new ParseError.GotValue.Token(state.Item2[0]), state.Item2[0].Location, state))
                            select result);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return prev.Bind(state => ParseState.Throw(new ParseError(new ParseError.ExpectedValue.Token(token), new ParseError.GotValue.EOF(), state.Item1[state.Item1.Count - 1].Location, state)));
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
                    ? ParseState.Throw(new ParseError(new ParseError.ExpectedValue.Option(options.Select(o => o.Item2).ToArray()), new ParseError.GotValue.EOF(), state.Item1[state.Item1.Count - 1].Location, state))
                    : ParseState.Throw(new ParseError(new ParseError.ExpectedValue.Option(options.Select(o => o.Item2).ToArray()), new ParseError.GotValue.None(), state.Item2[0].Location, state)));
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

        public static ParseState Rule(this ParseState prev, int rulenum) => prev.Map(state => Tuple.Create(state.Item1, state.Item2, new AST(new Term.NonTerminal(rulenum), state.Item3.Children)));

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
    public abstract class Term
    {
        private Term() { }
        public sealed class Terminal : Term
        {
            public Token Token { get; }
            public Terminal(Token token)
            {
                Token = token;
            }
        }
        public sealed class NonTerminal : Term
        {
            public int RuleNumber { get; }
            public NonTerminal(int ruleNumber)
            {
                RuleNumber = ruleNumber;
            }
        }
        public sealed class Loop : Term
        {
            public int RuleNumber { get; }
            public Loop(int ruleNumber)
            {
                RuleNumber = ruleNumber;
            }
        }
        public sealed class EndLoop : Term
        {
            public EndLoop() { }
        }
        public T Match<T>(Func<Token, T> Terminal, Func<int, T> NonTerminal, Func<int, T> Loop, Func<T> EndLoop)
        {
            if      (this is Terminal)    return Terminal   ((this as Terminal)   .Token);
            else if (this is NonTerminal) return NonTerminal((this as NonTerminal).RuleNumber);
            else if (this is Loop)        return Loop       ((this as Loop)       .RuleNumber);
            else if (this is EndLoop)     return EndLoop();
            else throw new Exception("Term object was not one of Terminal, NonTerminal, Loop, EndLoop.");
        }
        public TermState State => this.Match(Terminal: _ => TermState.Terminal,
                                             NonTerminal: _ => TermState.NonTerminal,
                                             Loop: _ => TermState.Loop,
                                             EndLoop: () => TermState.EndLoop);
        public override string ToString() => this.Match(Terminal: tok => "Terminal " + tok.ToString(), NonTerminal: nt => "Nonterminal " + nt, Loop: l => "Loop " + l, EndLoop: () => "EndLoop");
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
        public abstract class ExpectedValue
        {
            // ExpectedValue = EOF | Token int | Option string[]
            private ExpectedValue() { }
            public sealed class EOF : ExpectedValue
            {
                public EOF() { }
            }
            public sealed class Token : ExpectedValue
            {
                public int Type { get; }
                public Token(int type)
                {
                    Type = type;
                }
            }
            public sealed class Option : ExpectedValue
            {
                public string[] Options { get; }
                public Option(params string[] options)
                {
                    Options = options;
                }
            }
            public T Match<T>(Func<T> EOF, Func<int, T> Token, Func<string[], T> Option)
            {
                if      (this is EOF)    return EOF();
                else if (this is Token)  return Token ((this as Token) .Type);
                else if (this is Option) return Option((this as Option).Options);
                else throw new Exception("ExpectedValue object was not one of EOF, Token, Option");
            }
            public ExpectedValueState State => this.Match(EOF: () => ExpectedValueState.EOF,
                                                          Token: _ => ExpectedValueState.Token,
                                                          Option: _ => ExpectedValueState.Option);
            public override string ToString() => this.Match(EOF: () => "EOF", Token: tok => "Token " + tok, Option: os => "Option [" + string.Join(", ", os) + "]");
        }
        public enum ExpectedValueState
        {
            EOF, Token, Option
        }
        public class GotValue
        {
            // GotValue = EOF | Token Token | None
            private GotValue() { }
            public sealed class EOF : GotValue
            {
                public EOF() { }
            }
            public sealed class Token : GotValue
            {
                public MParse.Lexer.Token Type { get; }
                public Token(MParse.Lexer.Token type)
                {
                    Type = type;
                }
            }
            public sealed class None : GotValue
            {
                public None() { }
            }
            public T Match<T>(Func<T> EOF, Func<MParse.Lexer.Token, T> Token, Func<T> None)
            {
                if      (this is EOF)   return EOF();
                else if (this is Token) return Token((this as Token).Type);
                else if (this is None)  return None();
                else throw new Exception("ExpectedValue object was not one of EOF, Token, Option");
            }
            public GotValueState State => this.Match(EOF: () => GotValueState.EOF,
                                                          Token: _ => GotValueState.Token,
                                                          None: () => GotValueState.None);
            public override string ToString() => this.Match(EOF: () => "EOF", Token: tok => "Token " + tok, None: () => "None");
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
