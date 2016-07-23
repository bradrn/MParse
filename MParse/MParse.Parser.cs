using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlgebraicTypes;
using MParse.Lexer;

using TokenList = System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>;
using ParseState = AlgebraicTypes.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<MParse.Lexer.Token, int>>>, MParse.Lexer.TokenError>;
using NonTerminal = System.Func<AlgebraicTypes.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<MParse.Lexer.Token, int>>>, MParse.Lexer.TokenError>,
                                AlgebraicTypes.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<MParse.Lexer.Token, int>>>, MParse.Lexer.TokenError>>;
using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.Parser.TermSpecification>>;
using AST = MParse.Parser.Tree<AlgebraicTypes.Either<MParse.Lexer.Token, int>>;

namespace MParse.Parser
{
    public static class Parser
    {
        public static Error<AST, TokenError> DoParse(NonTerminal Start, TokenList input, ASTMap map)
        {
            ParseState parsed = ParseState.Return(Tuple.Create(TokenList.Empty, input, ImmutableList<Either<Token, int>>.Empty)).Parse(Start);
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
                                                               state.Item3.Add(Either<Token, int>.Left(state.Item2[0]))))
                                           : ParseState.Throw(new TokenError(TokenError.ExpectedValue.Token(token), TokenError.GotValue.Token(state.Item2[0]), state.Item2[0].Location))
                            select result);
                }
                catch (ArgumentOutOfRangeException)
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
                                                                                                          state.Item3.Add(Either<Token, int>.Right(rulenum)))));

        public static NonTerminal Rule(this NonTerminal nt, int rulenum) => prev => prev.Parse(nt).Rule(rulenum);

        public static ParseState epsilon(ParseState text) => text.Rule(-1);

        public static Tuple<string, int> Specifier(string s, int i) => Tuple.Create(s, i);

        public static Token Token(int type, string value, ILocation location) => new Token(type, value, location);

        public static AST ProcessAST(ImmutableList<Either<Token, int>> log, ASTMap map)
        {
            Tree<TermSpecification> _ast = new Tree<TermSpecification>();
            List<int> position = new List<int>();
            bool first = true;
            foreach (int nt in log.Reverse().Where(item => item.State == EitherState.Right).Select(item => item.Match(Left: tok => 0, Right: nt => nt)))
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
            /*foreach (Token tok in log.Reverse().Where(item => item.State == EitherState.Left).Select(item => item.Match(Left: tok => tok, Right: nt => new Token())))
            {
                _ast.Rightmost(ts => ts.State == TermSpecificationState.Terminal)
                    .Match
                    (
                        Just: rightmost =>
                        {
                            _ast.Navigate(rightmost).Value = TermSpecification.Terminal(;
                            _ast.Navigate(rightmost).Children = map[map.Keys.Where(s => s.Item2 == nt).First()]
                                                                .Select(t => new Tree<TermSpecification>(t))
                                                                .ToList();
                            return Unit.Nil;
                        },
                        Nothing: () => Unit.Nil
                    );
            }*/
            return FromTree(_ast, log, map);
        }

        public static ASTMap Initialise(this ASTMap map)
        {
            ASTMap _map = new ASTMap(map);
            _map.Add(Specifier(nameof(epsilon), -1), new List<TermSpecification>());
            return _map;
        }

        private static AST FromTree(Tree<TermSpecification> t, ImmutableList<Either<Token, int>> log, ASTMap map, int position = 0)
        {
            Func<int, string> GetNonTerminal = nt => map.Keys.Where(s => s.Item2 == nt)
                                                             .Select(s => s.Item1)
                                                             .First();
            ImmutableList<Token> tokenLog = log.Reverse().Where(item => item.State == EitherState.Left).Select(item => item.Match(Left: tok => tok, Right: nt => new Token())).ToImmutableList();
            bool wasTerminal = false;

            AST ast = new AST();
            ast.Value = t.Value.Match
                        (
                            Terminal: i =>
                            {
                                if (tokenLog[position].Type != i) throw new Exception("Terminals did not match");
                                wasTerminal = true;
                                return Either<Token, int>.Left(tokenLog[position]);
                            },
                            NonTerminal: nt => Either<Token, int>.Right(nt),
                            Option: os => {
                                if (t.Children.Count == 1) return t.Children[0].Value.Match(Terminal: _ => { throw new Exception("Cannot have a Terminal as a child of an Option"); },
                                                                                            NonTerminal: nt => Either<Token, int>.Right(nt),
                                                                                            Option: _ => { throw new Exception("Cannot have an Option as a child of an Option"); });
                                else throw new Exception("More than one child of Option");
                            }
                        );
            ast.Children = ast.Value.Match
                                     (
                                        Left: _ => new List<AST>(),
                                        Right: nt => nt == -1 ? new List<AST>() : t.Children.Select(child => FromTree(child, log, map, position + (wasTerminal ? 1 : 0))).ToList()
                                     );
            return ast;
        }
    }

    #region TermSpecification
    // TermSpecification = Terminal int | NonTerminal int | Option int int
    public class TermSpecification
    {
        private class TerminalImpl
        {
            public int Value1 { get; }
            public TerminalImpl(int value1)
            {
                Value1 = value1;
            }
        }
        private class NonTerminalImpl
        {
            public int Value1 { get; }
            public NonTerminalImpl(int value1)
            {
                Value1 = value1;
            }
        }
        private class OptionImpl
        {
            public ImmutableList<int> Values { get; }
            public OptionImpl(ImmutableList<int> vs)
            {
                Values = vs;
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
        public static TermSpecification Terminal(int value1)
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
        public static TermSpecification Option(params int[] values)
        {
            TermSpecification result = new TermSpecification();
            result.OptionValue = new OptionImpl(values.ToImmutableList());
            return result;
        }
        public T1 Match<T1>(Func<int, T1> Terminal, Func<int, T1> NonTerminal, Func<ImmutableList<int>, T1> Option)
        {
            switch (State)
            {
                case TermSpecificationState.Terminal: return Terminal(TerminalValue.Value1);
                case TermSpecificationState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
                case TermSpecificationState.Option: return Option(OptionValue.Values);
            }
            return default(T1);
        }
        public override string ToString() => this.Match(Terminal: t => "Terminal " + t,
                                                        NonTerminal: nt => "Nonterminal " + nt,
                                                        Option: os => "Option " + string.Join(" ", os.Select(o => o.ToString())));
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
}
