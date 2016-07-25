#define PRINT_INTERMEDIATE_RESULTS

using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSFunc.Types;
using MParse.Lexer;

using TokenList = System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>;
using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<CSFunc.Types.Either<MParse.Lexer.Token, int>>>, MParse.Lexer.TokenError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<CSFunc.Types.Either<MParse.Lexer.Token, int>>>, MParse.Lexer.TokenError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<CSFunc.Types.Either<MParse.Lexer.Token, int>>>, MParse.Lexer.TokenError>>;
using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.Parser.TermSpecification>>;
using AST = MParse.Parser.Tree<CSFunc.Types.Either<MParse.Lexer.Token, int>>;

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
#if PRINT_INTERMEDIATE_RESULTS
            parsed.Map(value =>
            {
                value.Item3.Reverse().ForEach(item => item.Match(Left: tok => { Console.WriteLine(tok.ToString()); return Unit.Nil; },
                                                     Right: nt => { Console.WriteLine(nt); return Unit.Nil; })); return Unit.Nil;
            });
#endif
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

        public static ASTMap Initialise(this ASTMap map)
        {
            ASTMap _map = new ASTMap(map);
            _map.Add(Specifier(nameof(epsilon), -1), new List<TermSpecification>() { });
            return _map;
        }

        public static AST ProcessAST(ImmutableList<Either<Token, int>> log, ASTMap map)
        {
            Tree<IntermediateASTEntry> _ast = new Tree<IntermediateASTEntry>();
            List<int> position = new List<int>();
            bool first = true;
            foreach (Either<Token, int> item in log.Reverse()) //.Where(item => item.State == EitherState.Right).Select(item => item.Match(Left: tok => 0, Right: nt => nt)))
            {
                if (first)
                {
                    item.Match(Right: nt =>
                    {
                        _ast.Value = IntermediateASTEntry.NonTerminal(nt);
                        _ast.Children = map[map.Keys.Where(s => s.Item2 == nt).First()]
                                        .Select(t => new Tree<IntermediateASTEntry>(t.Match(
                                                        Terminal: i => IntermediateASTEntry.TerminalRoot(i),
                                                        NonTerminal: _nt => IntermediateASTEntry.NonTerminal(_nt),
                                                        Option: os => IntermediateASTEntry.Option(os))))
                                        .ToList();
                        first = false;
                        return Unit.Nil;
                    }, Left: tok => Unit.Nil);
                }
                else
                {
                    item.Match
                    (                                    
                        Right: nt =>                     
                            _ast.Rightmost(ts => (ts.State == IntermediateASTEntryState.NonTerminal) || (ts.State == IntermediateASTEntryState.Option))
                            .Match                       
                            (                            
                                Just: rightmost =>       
                                {
                                    if (nt == -1)
                                    {
                                        _ast.Navigate(rightmost).Value = IntermediateASTEntry.Epsilon();
                                        _ast.Navigate(rightmost).Children = new List<Tree<IntermediateASTEntry>>();
                                    }
                                    else
                                    {
                                        _ast.Navigate(rightmost).Value = IntermediateASTEntry.NonTerminal(nt);
                                        _ast.Navigate(rightmost).Children = map[map.Keys.Where(s => s.Item2 == nt).First()]
                                                                            .Select(t => new Tree<IntermediateASTEntry>(t.Match(
                                                                                            Terminal: i => IntermediateASTEntry.TerminalRoot(i),
                                                                                            NonTerminal: _nt => IntermediateASTEntry.NonTerminal(_nt),
                                                                                            Option: os => IntermediateASTEntry.Option(os))))
                                                                            .ToList();
                                    }
                                    return Unit.Nil;
                                },
                                Nothing: () => Unit.Nil
                            ),
                        Left: tok =>
                            _ast.Rightmost(ts => (ts.State == IntermediateASTEntryState.TerminalRoot))
                                .Match
                                (
                                    Just: rightmost =>
                                    {
                                        _ast.Navigate(rightmost).Value = IntermediateASTEntry.TerminalLeaf(tok);
                                        _ast.Navigate(rightmost).Children = new List<Tree<IntermediateASTEntry>>();
                                        return Unit.Nil;
                                    },
                                    Nothing: () => Unit.Nil
                                )
                    );
                }
            }

#if PRINT_INTERMEDIATE_RESULTS
            PrintPretty(_ast, "", true);
#endif

            return FromTree(_ast);
        }

        private static AST FromTree(Tree<IntermediateASTEntry> _ast)
        {
            AST ast = new AST();
            ast.Value = _ast.Value.Match
                        (
                            TerminalLeaf: s => Either<Token, int>.Left(s),
                            TerminalRoot: _ => { throw new Exception(); },
                            NonTerminal: nt => Either<Token, int>.Right(nt),
                            Option: os =>
                            {
                                if (_ast.Children.Count == 1) return _ast.Children[0].Value.Match(TerminalRoot: _ => { throw new Exception(); },
                                                                                                  TerminalLeaf: _ => { throw new Exception(); },
                                                                                                  NonTerminal: nt => Either<Token, int>.Right(nt),
                                                                                                  Option: _ => FromTree(_ast.Children[0]).Value,
                                                                                                  Epsilon: () => { throw new Exception(); });
                                else throw new Exception();
                            },
                            Epsilon: () => Either<Token, int>.Right(-1)
                        );
            ast.Children = ast.Value.Match(
                Left: _ => new List<AST>(),
                Right: nt => _ast.Children.Select(child => FromTree(child)).ToList());
            return ast;
        }


        // FOR DEBUGGING PURPOSES ONLY! WILL ONLY BE INVOKED IF PRINT_INTERMEDIATE_RESULTS IS DEFINED
#if PRINT_INTERMEDIATE_RESULTS
        private static void ClearLine() // Thanks to SomeNameNoFake from http://stackoverflow.com/questions/8946808/can-console-clear-be-used-to-only-clear-a-line-instead-of-whole-console
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, Console.CursorTop - (Console.WindowWidth >= Console.BufferWidth ? 1 : 0));
        }

        private static void PrintPretty<T>(this Tree<T> t, string indent, bool last) // With thanks to Will from http://stackoverflow.com/questions/1649027/how-do-i-print-out-a-tree-structure
        {
            ClearLine();
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
            Console.WriteLine(t.Value.ToString());
            for (int i = 0; i < t.Children.Count; i++)
                t.Children[i].PrintPretty(indent, i == t.Children.Count - 1);

        }
#endif
    }

    #region TermSpecification
    // TermSpecification = Terminal int | NonTerminal int | Option ImmutableList<int>
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

    #region IntermediateASTEntry
    // IntermediateASTEntry = TerminalRoot int | TerminalLeaf Token | NonTerminal int | Option ImmutableList<int> | Epsilon
    public class IntermediateASTEntry
    {
        private class TerminalRootImpl
        {
            public int Value1 { get; set; } = default(int);
            public TerminalRootImpl(int value1)
            {
                Value1 = value1;
            }
        }
        private class TerminalLeafImpl
        {
            public Token Value1 { get; set; } = default(Token);
            public TerminalLeafImpl(Token value1)
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
            public ImmutableList<int> Value1 { get; set; } = default(ImmutableList<int>);
            public OptionImpl(ImmutableList<int> value1)
            {
                Value1 = value1;
            }
        }
        private class EpsilonImpl
        {
            public EpsilonImpl()
            {
            }
        }
        public IntermediateASTEntryState State { get; set; }
        private TerminalRootImpl TerminalRootField;
        private TerminalRootImpl TerminalRootValue { get { return TerminalRootField; } set { TerminalRootField = value; TerminalLeafField = null; NonTerminalField = null; OptionField = null; EpsilonField = null; State = IntermediateASTEntryState.TerminalRoot; } }
        private TerminalLeafImpl TerminalLeafField;
        private TerminalLeafImpl TerminalLeafValue { get { return TerminalLeafField; } set { TerminalLeafField = value; TerminalRootField = null; NonTerminalField = null; OptionField = null; EpsilonField = null; State = IntermediateASTEntryState.TerminalLeaf; } }
        private NonTerminalImpl NonTerminalField;
        private NonTerminalImpl NonTerminalValue { get { return NonTerminalField; } set { NonTerminalField = value; TerminalRootField = null; TerminalLeafField = null; OptionField = null; EpsilonField = null; State = IntermediateASTEntryState.NonTerminal; } }
        private OptionImpl OptionField;
        private OptionImpl OptionValue { get { return OptionField; } set { OptionField = value; TerminalRootField = null; TerminalLeafField = null; NonTerminalField = null; EpsilonField = null; State = IntermediateASTEntryState.Option; } }
        private EpsilonImpl EpsilonField;
        private EpsilonImpl EpsilonValue { get { return EpsilonField; } set { EpsilonField = value; TerminalRootField = null; TerminalLeafField = null; NonTerminalField = null; OptionField = null; State = IntermediateASTEntryState.Epsilon; } }
        private IntermediateASTEntry() { }
        public static IntermediateASTEntry TerminalRoot(int value1)
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.TerminalRootValue = new TerminalRootImpl(value1);
            return result;
        }
        public static IntermediateASTEntry TerminalLeaf(Token value1)
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.TerminalLeafValue = new TerminalLeafImpl(value1);
            return result;
        }
        public static IntermediateASTEntry NonTerminal(int value1)
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.NonTerminalValue = new NonTerminalImpl(value1);
            return result;
        }
        public static IntermediateASTEntry Option(ImmutableList<int> value1)
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.OptionValue = new OptionImpl(value1);
            return result;
        }
        public static IntermediateASTEntry Epsilon()
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.EpsilonValue = new EpsilonImpl();
            return result;
        }
        public T1 Match<T1>(Func<int, T1> TerminalRoot, Func<Token, T1> TerminalLeaf, Func<int, T1> NonTerminal, Func<ImmutableList<int>, T1> Option, Func<T1> Epsilon)
        {
            switch (State)
            {
                case IntermediateASTEntryState.TerminalRoot: return TerminalRoot(TerminalRootValue.Value1);
                case IntermediateASTEntryState.TerminalLeaf: return TerminalLeaf(TerminalLeafValue.Value1);
                case IntermediateASTEntryState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
                case IntermediateASTEntryState.Option: return Option(OptionValue.Value1);
                case IntermediateASTEntryState.Epsilon: return Epsilon();
            }
            return default(T1);
        }
        public override string ToString() => this.Match(TerminalRoot: t => "Root Terminal " + t,
                                                                TerminalLeaf: l => "Leaf Terminal " + l.ToString(),
                                                                NonTerminal: nt => "Nonterminal " + nt,
                                                                Option: os => "Option " + string.Join(" ", os.Select(o => o.ToString())),
                                                                Epsilon: () => "Epsilon");
    }
    public enum IntermediateASTEntryState
    {
        TerminalRoot, TerminalLeaf, NonTerminal, Option, Epsilon
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
