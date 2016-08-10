using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSFunc.Types;
using MParse.Lexer;

using TokenList = System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>;
using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Parser.Term>>, MParse.Parser.ParseError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Parser.Term>>, MParse.Parser.ParseError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Parser.Term>>, MParse.Parser.ParseError>>;
using ASTMap = System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<MParse.Parser.TermSpecification>>;
using AST = MParse.Parser.Tree<MParse.Parser.Term>;

namespace MParse.Parser
{
    public static class Parser
    {
        public static Error<AST, ParseError> DoParse(NonTerminal Start, TokenList input, ASTMap map, bool showIntermediateResults = false)
        {
            ParseState parsed = ParseState.Return(Tuple.Create(TokenList.Empty, input, ImmutableList<Term>.Empty)).Parse(Start);
            parsed = parsed.Bind(state => state.Item2.Count == 0
                                          ? parsed
                                          : ParseState.Throw(new ParseError(ParseError.ExpectedValue.EOF(), ParseError.GotValue.Token(state.Item2[0]), state.Item2[0].Location, state)));
            return parsed.Map(value => ProcessAST(value.Item3, map, showIntermediateResults));
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
                                                               state.Item3.Add(Term.Terminal(state.Item2[0]))))
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
            if (prev.State == ErrorState.Result) return group(prev);
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
                ParseState parsed = prev.AddToLog(Term.EndLoop()); // Add EndLoop at the start of loop so that when the log is reversed, it comes at the end
                while (true)
                {
                    ParseState _parsed = parsed.Parse(nt);
                    if (_parsed.State == ErrorState.Throw) return parsed;
                    else parsed = _parsed;
                }
            }
            else return prev;
        }

        public static ParseState Rule(this ParseState prev, int rulenum) => prev.AddToLog(Term.NonTerminal(rulenum));

        public static NonTerminal Rule(this NonTerminal nt, int rulenum) => prev => prev.Parse(nt).Rule(rulenum);

        // Methods to convert from lists of Terms to trees

        public static AST ProcessAST(ImmutableList<Term> log, ASTMap map, bool showIntermediateResults = false)
        {
            Tree<IntermediateASTEntry> _ast = new Tree<IntermediateASTEntry>();
            List<int> position = new List<int>();
            bool first = true;
            int line = Console.CursorTop;
            foreach (Term item in log.Reverse())
            {
                if (showIntermediateResults) Console.CursorTop = line;
                if (first)
                {
                    item.Match(NonTerminal: nt =>
                    {
                        _ast.Value = IntermediateASTEntry.NonTerminal(nt);
                        _ast.Children = map[map.Keys.Where(s => s == nt).First()]
                                        .Select(t => new Tree<IntermediateASTEntry>(t.Match(
                                                        Terminal: i => IntermediateASTEntry.TerminalRoot(i),
                                                        NonTerminal: _nt => IntermediateASTEntry.NonTerminal(_nt),
                                                        Option: os => IntermediateASTEntry.Option(os),
                                                        Loop: l => IntermediateASTEntry.Loop(l))))
                                        .ToList();
                        first = false;
                        return Unit.Nil;
                    }, Terminal: tok => Unit.Nil, Loop: l => Unit.Nil, EndLoop: () => Unit.Nil);
                }
                else
                {
                    item.Match
                    (
                        NonTerminal: nt =>
                            _ast.Rightmost(tree => (tree.IsLeaf && ((tree.Value.State == IntermediateASTEntryState.NonTerminal) ||
                                                                    (tree.Value.State == IntermediateASTEntryState.Option)))
                                                || (!tree.Children.Any(c => c.Value.State == IntermediateASTEntryState.EndLoop) && (tree.Value.State == IntermediateASTEntryState.Loop)))
                            .Match
                            (
                                Just: rightmost =>
                                {
                                    if (_ast.Navigate(rightmost).Value.State != IntermediateASTEntryState.Loop)
                                    {
                                        if (nt == -1)
                                        {
                                            _ast.Navigate(rightmost).Value = IntermediateASTEntry.Epsilon();
                                            _ast.Navigate(rightmost).Children = new List<Tree<IntermediateASTEntry>>();
                                        }
                                        else
                                        {
                                            _ast.Navigate(rightmost).Value = IntermediateASTEntry.NonTerminal(nt);
                                            _ast.Navigate(rightmost).Children = map[map.Keys.Where(s => s == nt).First()]
                                                                                .Select(t => new Tree<IntermediateASTEntry>(t.Match(
                                                                                                Terminal: i => IntermediateASTEntry.TerminalRoot(i),
                                                                                                NonTerminal: _nt => IntermediateASTEntry.NonTerminal(_nt),
                                                                                                Option: os => IntermediateASTEntry.Option(os),
                                                                                                Loop: l => IntermediateASTEntry.Loop(l))))
                                                                                .ToList();
                                        }
                                    }
                                    else
                                    {
                                        _ast.Navigate(rightmost).Value.Match(TerminalRoot: _ => Unit.Nil, TerminalLeaf: _ => Unit.Nil, NonTerminal: _ => Unit.Nil, Option: _ => Unit.Nil, EndLoop: () => Unit.Nil, Epsilon: () => Unit.Nil,
                                            Loop: l =>
                                            {
                                                _ast.Navigate(rightmost).Children.Insert(0, new Tree<IntermediateASTEntry>(IntermediateASTEntry.NonTerminal(l)));
                                                _ast.Navigate(rightmost).Children[0].Children = map[map.Keys.Where(s => s == nt).First()]
                                                                                                .Select(t => new Tree<IntermediateASTEntry>(t.Match(
                                                                                                                Terminal: i => IntermediateASTEntry.TerminalRoot(i),
                                                                                                                NonTerminal: _nt => IntermediateASTEntry.NonTerminal(_nt),
                                                                                                                Option: os => IntermediateASTEntry.Option(os),
                                                                                                                Loop: l2 => IntermediateASTEntry.Loop(l2))))
                                                                                                .ToList();
                                                return Unit.Nil;
                                            });
                                    }
                                    return Unit.Nil;
                                },
                                Nothing: () => Unit.Nil
                            ),
                        Terminal: tok =>
                            _ast.Rightmost(tree => tree.IsLeaf && (tree.Value.State == IntermediateASTEntryState.TerminalRoot))
                                .Match
                                (
                                    Just: rightmost =>
                                    {
                                        _ast.Navigate(rightmost).Value = IntermediateASTEntry.TerminalLeaf(tok);
                                        _ast.Navigate(rightmost).Children = new List<Tree<IntermediateASTEntry>>();
                                        return Unit.Nil;
                                    },
                                    Nothing: () => Unit.Nil
                                ),
                        Loop: l => Unit.Nil,
                        EndLoop: () =>
                            _ast.Rightmost(tree => !tree.Children.Any(c => c.Value.State == IntermediateASTEntryState.EndLoop) && (tree.Value.State == IntermediateASTEntryState.Loop))
                                .Match
                                (
                                    Just: rightmost =>
                                    {
                                        _ast.Navigate(rightmost).Children.Add(new Tree<IntermediateASTEntry>(IntermediateASTEntry.EndLoop(), new List<Tree<IntermediateASTEntry>>()));
                                        return Unit.Nil;
                                    },
                                    Nothing: () => Unit.Nil
                                )
                    );
                }
                if (showIntermediateResults) { _ast.PrintPretty(); Console.ReadLine(); }
            }

            return FromTree(_ast);
        }

        private static AST FromTree(Tree<IntermediateASTEntry> _ast)
        {
            AST ast = new AST();
            ast.Value = _ast.Value.Match
                        (
                            TerminalLeaf: s => Term.Terminal(s),
                            TerminalRoot: _ => { throw new Exception(); },
                            NonTerminal: nt => Term.NonTerminal(nt),
                            Option: os =>
                            {
                                if (_ast.Children.Count == 1) return _ast.Children[0].Value.Match(TerminalRoot: _ => { throw new Exception(); },
                                                                                                  TerminalLeaf: _ => { throw new Exception(); },
                                                                                                  NonTerminal: nt => Term.NonTerminal(nt),
                                                                                                  Option: _ => FromTree(_ast.Children[0]).Value,
                                                                                                  Loop: _ => { throw new Exception(); },
                                                                                                  EndLoop: () => { throw new Exception(); },
                                                                                                  Epsilon: () => { throw new Exception(); });
                                else throw new Exception();
                            },
                            Loop: l => Term.Loop(l),
                            EndLoop: () => Term.NonTerminal(-1),
                            Epsilon: () => Term.NonTerminal(-1)
                        );
            ast.Children = ast.Value.Match(
                Terminal: _ => new List<AST>(),
                NonTerminal: nt => _ast.Children.Select(child => FromTree(child)).ToList(),
                Loop: l => _ast.Children.Select(child => FromTree(child)).ToList(),
                EndLoop: () => new List<AST>());
            return ast;
        }

        // Utility methods

        public static ParseState epsilon(ParseState text) => text.Rule(-1);

        public static Token Token(int type, string value, ILocation location) => new Token(type, value, location);

        public static ParseState AddToLog(this ParseState prev, Term t) => prev.Map(state => Tuple.Create(state.Item1, state.Item2, state.Item3.Add(t)));

        public static Maybe<Token[]> Lookahead(this ParseState p, int n) => p.Match(Result: state => (state.Item2.Count < n) ? Maybe<Token[]>.Nothing()
                                                                                                                       : Maybe<Token[]>.Just(state.Item2.Take(n).ToArray()),
                                                                                    Throw: terr => Maybe<Token[]>.Nothing());

        public static ASTMap Initialise(this ASTMap map)
        {
            ASTMap _map = new ASTMap(map);
            _map.Add(-1, new List<TermSpecification>() { });
            return _map;
        }
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

    #region TermSpecification
    // TermSpecification = Terminal int | NonTerminal int | Option ImmutableList<int> | Loop int
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
        private class LoopImpl
        {
            public int Value1 { get; }
            public LoopImpl(int value1)
            {
                Value1 = value1;
            }
        }
        public TermSpecificationState State { get; set; }
        private TerminalImpl TerminalField;
        private TerminalImpl TerminalValue { get { return TerminalField; } set { TerminalField = value; NonTerminalField = null; OptionField = null; LoopField = null; State = TermSpecificationState.Terminal; } }
        private NonTerminalImpl NonTerminalField;
        private NonTerminalImpl NonTerminalValue { get { return NonTerminalField; } set { NonTerminalField = value; TerminalField = null; OptionField = null; LoopField = null; State = TermSpecificationState.NonTerminal; } }
        private OptionImpl OptionField;
        private OptionImpl OptionValue { get { return OptionField; } set { OptionField = value; TerminalField = null; NonTerminalField = null; LoopField = null; State = TermSpecificationState.Option; } }
        private LoopImpl LoopField;
        private LoopImpl LoopValue { get { return LoopField; } set { LoopField = value;  TerminalField = null; NonTerminalField = null; OptionField = null; State = TermSpecificationState.Loop; } }
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
        public static TermSpecification Loop(int value1)
        {
            TermSpecification result = new TermSpecification();
            result.LoopValue = new LoopImpl(value1);
            return result;
        }
        public T1 Match<T1>(Func<int, T1> Terminal, Func<int, T1> NonTerminal, Func<ImmutableList<int>, T1> Option, Func<int, T1> Loop)
        {
            switch (State)
            {
                case TermSpecificationState.Terminal: return Terminal(TerminalValue.Value1);
                case TermSpecificationState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
                case TermSpecificationState.Option: return Option(OptionValue.Values);
                case TermSpecificationState.Loop: return Loop(LoopValue.Value1);
            }
            return default(T1);
        }
        public override string ToString() => this.Match(Terminal: t => "Terminal " + t,
                                                        NonTerminal: nt => "Nonterminal " + nt,
                                                        Option: os => "Option " + string.Join(" ", os.Select(o => o.ToString())),
                                                        Loop: l => "Loop " + l);
    }
    public enum TermSpecificationState
    {
        Terminal, NonTerminal, Option, Loop
    }
    #endregion

    #region IntermediateASTEntry
    // IntermediateASTEntry = TerminalRoot int | TerminalLeaf Token | NonTerminal int | Option ImmutableList<int> | Loop int | EndLoop | Epsilon
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
        private class EpsilonImpl
        {
            public EpsilonImpl()
            {
            }
        }
        public IntermediateASTEntryState State { get; set; }
        private TerminalRootImpl TerminalRootField;
        private TerminalRootImpl TerminalRootValue { get { return TerminalRootField; } set { TerminalRootField = value; TerminalLeafField = null; NonTerminalField = null; OptionField = null; LoopField = null; EndLoopField = null; EpsilonField = null; State = IntermediateASTEntryState.TerminalRoot; } }
        private TerminalLeafImpl TerminalLeafField;
        private TerminalLeafImpl TerminalLeafValue { get { return TerminalLeafField; } set { TerminalLeafField = value; TerminalRootField = null; NonTerminalField = null; OptionField = null; LoopField = null; EndLoopField = null; EpsilonField = null; State = IntermediateASTEntryState.TerminalLeaf; } }
        private NonTerminalImpl NonTerminalField;
        private NonTerminalImpl NonTerminalValue { get { return NonTerminalField; } set { NonTerminalField = value; TerminalRootField = null; TerminalLeafField = null; OptionField = null; LoopField = null; EndLoopField = null; EpsilonField = null; State = IntermediateASTEntryState.NonTerminal; } }
        private OptionImpl OptionField;
        private OptionImpl OptionValue { get { return OptionField; } set { OptionField = value; TerminalRootField = null; TerminalLeafField = null; NonTerminalField = null; LoopField = null; EndLoopField = null; EpsilonField = null; State = IntermediateASTEntryState.Option; } }
        private LoopImpl LoopField;
        private LoopImpl LoopValue { get { return LoopField; } set { LoopField = value; TerminalRootField = null; TerminalLeafField = null; NonTerminalField = null; OptionField = null; EndLoopField = null; EpsilonField = null; State = IntermediateASTEntryState.Loop; } }
        private EndLoopImpl EndLoopField;
        private EndLoopImpl EndLoopValue { get { return EndLoopField; } set { EndLoopField = value; TerminalRootField = null; TerminalLeafField = null; NonTerminalField = null; OptionField = null; LoopField = null; EpsilonField = null; State = IntermediateASTEntryState.EndLoop; } }
        private EpsilonImpl EpsilonField;
        private EpsilonImpl EpsilonValue { get { return EpsilonField; } set { EpsilonField = value; TerminalRootField = null; TerminalLeafField = null; NonTerminalField = null; OptionField = null; LoopField = null; EndLoopField = null; State = IntermediateASTEntryState.Epsilon; } }
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
        public static IntermediateASTEntry Loop(int value1)
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.LoopValue = new LoopImpl(value1);
            return result;
        }
        public static IntermediateASTEntry EndLoop()
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.EndLoopValue = new EndLoopImpl();
            return result;
        }
        public static IntermediateASTEntry Epsilon()
        {
            IntermediateASTEntry result = new IntermediateASTEntry();
            result.EpsilonValue = new EpsilonImpl();
            return result;
        }
        public T1 Match<T1>(Func<int, T1> TerminalRoot, Func<Token, T1> TerminalLeaf, Func<int, T1> NonTerminal, Func<ImmutableList<int>, T1> Option, Func<int, T1> Loop, Func<T1> EndLoop, Func<T1> Epsilon)
        {
            switch (State)
            {
                case IntermediateASTEntryState.TerminalRoot: return TerminalRoot(TerminalRootValue.Value1);
                case IntermediateASTEntryState.TerminalLeaf: return TerminalLeaf(TerminalLeafValue.Value1);
                case IntermediateASTEntryState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
                case IntermediateASTEntryState.Option: return Option(OptionValue.Value1);
                case IntermediateASTEntryState.Loop: return Loop(LoopValue.Value1);
                case IntermediateASTEntryState.EndLoop: return EndLoop();
                case IntermediateASTEntryState.Epsilon: return Epsilon();
            }
            return default(T1);
        }
        public override string ToString() => this.Match(TerminalRoot: t => "Root Terminal " + t,
                                                                TerminalLeaf: l => "Leaf Terminal " + l.ToString(),
                                                                NonTerminal: nt => "Nonterminal " + nt,
                                                                Option: os => "Option " + string.Join(" ", os.Select(o => o.ToString())),
                                                                Loop: l => "Loop " + l,
                                                                EndLoop: () => "EndLoop",
                                                                Epsilon: () => "Epsilon");
    }
    public enum IntermediateASTEntryState
    {
        TerminalRoot, TerminalLeaf, NonTerminal, Option, Loop, EndLoop, Epsilon
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

        public Tree<T1> Map<T1>(Func<T, T1> f) => new Tree<T1>(f(this.Value), this.Children.Select(c => c.Map(f)).ToList());

        public Tree<T1> MapTree<T1>(Func<Tree<T>, T1> f) => new Tree<T1>(f(this), this.Children.Select(c => c.MapTree(f)).ToList());

        public Tree<T> Navigate(ImmutableList<int> directions) => directions.Count == 0
                                                                  ? this
                                                                  : this.Children[directions[0]].Navigate(directions.Skip(1).ToImmutableList());

        private Tree<Tuple<T, ImmutableList<int>>> Direct(ImmutableList<int> acc) =>
            new Tree<Tuple<T, ImmutableList<int>>>(Tuple.Create(this.Value, acc), this.Children.Select((c, i) => c.Direct(acc.Add(i))).ToList());

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
        public Tuple<TokenList, TokenList, ImmutableList<Term>> Previous { get; set; }
        public ParseError(ExpectedValue expected, GotValue got, ILocation location, Tuple<TokenList, TokenList, ImmutableList<Term>> previous) : base(location)
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
