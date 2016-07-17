﻿using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlgebraicTypes;
using TokenList = System.Collections.Immutable.ImmutableList<MParse.Token>;
using ParseState = AlgebraicTypes.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Token>, System.Collections.Immutable.ImmutableList<MParse.Token>, System.Collections.Immutable.ImmutableList<int>>, MParse.TokenError>;
using NonTerminal = System.Func<AlgebraicTypes.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Token>, System.Collections.Immutable.ImmutableList<MParse.Token>, System.Collections.Immutable.ImmutableList<int>>, MParse.TokenError>,
                                AlgebraicTypes.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Token>, System.Collections.Immutable.ImmutableList<MParse.Token>, System.Collections.Immutable.ImmutableList<int>>, MParse.TokenError>>;
using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.TermSpecification>>;

namespace MParse
{
    public static class MParse
    {
        public static Error<AST, TokenError> DoParse(this NonTerminal Start, TokenList input, ASTMap map)
        {
            ParseState parsed = ParseState.Return(Tuple.Create(TokenList.Empty, input, ImmutableList<int>.Empty)).Parse(Start);
            parsed = parsed.Bind(state => state.Item2.Count == 0
                                          ? parsed
                                          : ParseState.Throw(new TokenError(TokenError.ExpectedValue.EOF(), TokenError.GotValue.Token(state.Item2[0]), state.Item2[0].Location)));
            return parsed.Map(value => ProcessAST(value.Item3, map));
        }

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
                                                               state.Item3))
                                           : ParseState.Throw(new TokenError(TokenError.ExpectedValue.Token(token), TokenError.GotValue.Token(state.Item2[0]), state.Item2[0].Location))
                            select result);
                }
                catch (IndexOutOfRangeException)
                {
                    return prev.Bind(state => ParseState.Throw(new TokenError(TokenError.ExpectedValue.Token(token), TokenError.GotValue.EOF(), state.Item1[state.Item1.Count - 1].Location)));
                }
            }
            else return prev;
        }

        public static ParseState Parse(this ParseState prev, Func<ParseState, ParseState> group)
        {
            if (prev.State == ErrorState.Result) return group(prev);
            else return prev;
        }

        public static Func<ParseState, ParseState> Option(params Tuple<Func<ParseState, ParseState>, string>[] options)
        {
            return prev =>
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
                    return prev.Bind(state => ParseState.Throw(new TokenError(TokenError.ExpectedValue.Option(options.Select(o => o.Item2).ToArray()), TokenError.GotValue.None(), state.Item1[state.Item1.Count - 1].Location)));
                }
                else return prev;
            };
        }

        public static Tuple<Func<ParseState, ParseState>, string> Option(Func<ParseState, ParseState> ps, string description) => Tuple.Create(ps, description);

        public static ParseState Rule(this ParseState prev, int rulenum) => prev.Bind(state => ParseState.Return(
                                                                                             Tuple.Create(state.Item1,
                                                                                                          state.Item2,
                                                                                                          state.Item3.Add(rulenum))));

        public static NonTerminal Rule(this NonTerminal nt, int rulenum) => prev => prev.Parse(nt).Rule(rulenum);

        public static ParseState epsilon(ParseState text) => text.Rule(-1);

        public static Tuple<string, int> Specifier(string s, int i) => Tuple.Create(s, i);

        public static Token Token(int type, string value, ILocation location) => new Token(type, value, location);

        public static AST ProcessAST(ImmutableList<int> log, ASTMap map)
        {
            Tree<TermSpecification> _ast = new Tree<TermSpecification>();
            List<int> position = new List<int>();
            bool first = true;
            foreach (int nt in log.Reverse())
            {
                if (first)
                {
                    _ast.Value = TermSpecification.NonTerminal(nt);
                    _ast.Children = map[map.Keys.Where(s => s.Item2 == nt).First()]
                                    .Select(t => new Tree<TermSpecification>(t))
                                    .ToList();
                    first = false;
                }
                else
                {
                    _ast.Rightmost(ts => (ts.State == TermSpecificationState.NonTerminal) || (ts.State == TermSpecificationState.Option))
                        .Match
                        (
                            Just: rightmost =>
                            {
                                if (_ast.Navigate(rightmost).Value.State == TermSpecificationState.Option)
                                    _ast.Navigate(rightmost).Value = TermSpecification.NonTerminal(nt);
                                _ast.Navigate(rightmost).Children = map[map.Keys.Where(s => s.Item2 == nt).First()]
                                                                    .Select(t => new Tree<TermSpecification>(t))
                                                                    .ToList();
                                return Unit.Nil;
                            },
                            Nothing: () => Unit.Nil
                        );
                }
            }
            return AST.FromTree(_ast, map);
        }

        public static void Initialise(this ASTMap map)
        {
            map.Add(Specifier(nameof(epsilon), -1), new List<TermSpecification> { TermSpecification.Terminal("") });
        }
    }
    
    public struct Token
    {
        public int Type { get; }
        public string Value { get; }
        public ILocation Location { get; }
        public Token(int type, string value, ILocation location)
        {
            Type = type;
            Value = value;
            Location = location;
        }
    }

    public class TokenError
    {
        public class ExpectedValue
        {
            // ExpectedValue = EOF | Token int | Option string[]
            private enum ExpectedValueState
            {
                EOF, Token, Option
            }
            private ExpectedValueState State { get; set; }
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
        public class GotValue
        {
            // GotValue = EOF | Token Token | None
            private enum GotValueState
            {
                EOF, Token, None
            }
            private GotValueState State { get; set; }
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
        public ExpectedValue Expected { get; }
        public GotValue Got { get; }
        public ILocation Location { get; }
        public TokenError(ExpectedValue expected, GotValue got, ILocation location)
        {
            Expected = expected;
            Got = got;
            Location = location;
        }
        public string ToString(Dictionary<int, string> tokenMap)
        {
            string value = "Error: Expected ";
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
            value += " at " + Location.ToString();
            return value;
        }
    }

    public interface ILocation { }

    #region Term
    // Term = Terminal string | NonTerminal string
    public class Term
    {
        private class TerminalImpl
        {
            public string Value1 { get; set; } = default(string);
            public TerminalImpl(string value1)
            {
                Value1 = value1;
            }
        }
        private class NonTerminalImpl
        {
            public string Value1 { get; set; } = default(string);
            public NonTerminalImpl(string value1)
            {
                Value1 = value1;
            }
        }
        public TermState State { get; set; }
        private TerminalImpl TerminalField;
        private TerminalImpl TerminalValue { get { return TerminalField; } set { TerminalField = value; NonTerminalField = null; State = TermState.Terminal; } }
        private NonTerminalImpl NonTerminalField;
        private NonTerminalImpl NonTerminalValue { get { return NonTerminalField; } set { NonTerminalField = value; TerminalField = null; State = TermState.NonTerminal; } }
        private Term() { }
        public static Term Terminal(string value1)
        {
            Term result = new Term();
            result.TerminalValue = new TerminalImpl(value1);
            return result;
        }
        public static Term NonTerminal(string value1)
        {
            Term result = new Term();
            result.NonTerminalValue = new NonTerminalImpl(value1);
            return result;
        }
        public T1 Match<T1>(Func<string, T1> Terminal, Func<string, T1> NonTerminal)
        {
            switch (State)
            {
                case TermState.Terminal: return Terminal(TerminalValue.Value1);
                case TermState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
            }
            return default(T1);
        }
        public override string ToString() => this.Match(Terminal: t => "Terminal " + t,
                                                        NonTerminal: nt => "Nonterminal " + nt);
    }
    public enum TermState
    {
        Terminal, NonTerminal
    }
    #endregion

    #region TermSpecification
    // TermSpecification = Terminal string | NonTerminal int | Option int int
    public class TermSpecification
    {
        private class TerminalImpl
        {
            public string Value1 { get; set; } = default(string);
            public TerminalImpl(string value1)
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
        private class OptionImpl
        {
            public int Value1 { get; set; } = default(int);
            public int Value2 { get; set; } = default(int);
            public OptionImpl(int value1, int value2)
            {
                Value1 = value1;
                Value2 = value2;
            }
        }
        public TermSpecificationState State { get; set; }
        private TerminalImpl TerminalField;
        private TerminalImpl TerminalValue { get { return TerminalField; } set { TerminalField = value; NonTerminalField = null; OptionField = null; State = TermSpecificationState.Terminal; } }
        private NonTerminalImpl NonTerminalField;
        private NonTerminalImpl NonTerminalValue { get { return NonTerminalField; } set { NonTerminalField = value; TerminalField = null; OptionField = null; State = TermSpecificationState.NonTerminal; } }
        private OptionImpl OptionField;
        private OptionImpl OptionValue { get { return OptionField; } set { OptionField = value; TerminalField = null; NonTerminalField = null; State = TermSpecificationState.Option; } }
        private TermSpecification() { }
        public static TermSpecification Terminal(string value1)
        {
            TermSpecification result = new TermSpecification();
            result.TerminalValue = new TerminalImpl(value1);
            return result;
        }
        public static TermSpecification NonTerminal(int value1)
        {
            TermSpecification result = new TermSpecification();
            result.NonTerminalValue = new NonTerminalImpl(value1);
            return result;
        }
        public static TermSpecification Option(int value1, int value2)
        {
            TermSpecification result = new TermSpecification();
            result.OptionValue = new OptionImpl(value1, value2);
            return result;
        }
        public T1 Match<T1>(Func<string, T1> Terminal, Func<int, T1> NonTerminal, Func<int, int, T1> Option)
        {
            switch (State)
            {
                case TermSpecificationState.Terminal: return Terminal(TerminalValue.Value1);
                case TermSpecificationState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
                case TermSpecificationState.Option: return Option(OptionValue.Value1, OptionValue.Value2);
            }
            return default(T1);
        }
    }
    public enum TermSpecificationState
    {
        Terminal, NonTerminal, Option
    }
    #endregion

    public class Tree<T>
    {
        public T Value;
        public List<Tree<T>> Children { get; set; }
        public bool IsLeaf => Children.Count == 0;

        public Tree() : this(default(T)) { }

        public Tree(T t) : this(t, new List<Tree<T>>()) { }

        public Tree(T t, List<Tree<T>> children)
        {
            Value = t;
            Children = children;
        }

        public Tree<T> Navigate(ImmutableList<int> directions) => directions.Count == 0
                                                                  ? this
                                                                  : this.Children[directions[0]].Navigate(directions.Skip(1).ToImmutableList());

        private static ImmutableList<NodeInfo> _lowermost(Tree<T> tree, ImmutableList<int> acc) => 
            tree.IsLeaf
            ? new List<NodeInfo> { new NodeInfo { Value = tree.Value, Directions = acc } }.ToImmutableList()
            : tree.Children.SelectMany((child, i) => _lowermost(child, acc.Add(i))).ToImmutableList();

        public Maybe<ImmutableList<int>> Rightmost(Func<T, bool> predicate)
        {
            ImmutableList<NodeInfo> lowermost = _lowermost(this, ImmutableList<int>.Empty);
            return lowermost.Aggregate(Maybe<ImmutableList<int>>.Nothing(), (acc, ni) => predicate(ni.Value)
                                                                                         ? Maybe<ImmutableList<int>>.Just(ni.Directions)
                                                                                         : acc);
        }

        private struct NodeInfo
        {
            public T Value;
            public ImmutableList<int> Directions;
        }
    }

    public class AST
    {
        public Term Term { get; set; }
        public List<AST> Children { get; set; }
        public bool IsLeaf => Children.Count == 0;

        public AST()
        {
            Term = null;
            Children = new List<AST>();
        }

        public AST(string terminal) : this(Term.Terminal(terminal), new List<AST>()) { }

        public AST(Term t, List<AST> children)
        {
            Term = t;
            Children = children;
        }

        public AST RemoveAll(Predicate<AST> p) => new AST(this.Term, this.Children.ToImmutableList().RemoveAll(p).Select(child => child.RemoveAll(p)).ToList());

        public static AST FromTree(Tree<TermSpecification> t, ASTMap map)
        {
            AST _ast = new AST();
            _ast.Term = t.Value.Match
                        (
                            Terminal: s => Term.Terminal(s),
                            NonTerminal: nt => Term.NonTerminal(GetNonTerminal(map, nt)),
                            Option: os => { if (t.Children.Count == 1) return t.Children[0].Value.Match(Terminal: _ => { throw new Exception(); },
                                                                                                        NonTerminal: nt => Term.NonTerminal(GetNonTerminal(map, nt)),
                                                                                                        Option: _ => AST.FromTree(t.Children[0], map).Term,
                                                                                                        Base: () => { throw new Exception(); },
                                                                                                        Ignore: () => { throw new Exception(); }); else throw new Exception(); },
                        );
            _ast.Children = _ast.Term.Match(Terminal: _ => new List<AST>(),
                NonTerminal: nt => nt == nameof(MParse.epsilon) ? new List<AST>() : t.Children.Select(child => FromTree(child, map)).ToList());
            return _ast;
        }

        private static string GetNonTerminal(ASTMap map, int nt) => map.Keys.Where(s => s.Item2 == nt)
                                                                         .Select(s => s.Item1)
                                                                         .First();
    }
}
