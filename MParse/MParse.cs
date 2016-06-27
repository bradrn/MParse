using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlgebraicTypes;
using ParseState = AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>;
using NonTerminal = System.Func<AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>,
                                AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>>;
using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.TermSpecification>>;

namespace MParse
{
    public static class MParse
    {
        public static ParseState Parse(this ParseState prev, char c)
        {
            if (prev.State == ErrorState.Result)
            {
                try
                {
                    return (from state in prev
                            from result in (state.Item2[0] == c)
                                           ? ParseState.Result(Tuple.Create(
                                                               state.Item1 + state.Item2[0],
                                                               string.Concat(state.Item2.Skip(1)),
                                                               state.Item3))
                                           : ParseState.Throw($"Error: Expected \"{c}\", but got \"{state.Item2[0]}\" near \"{state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}\"")
                            select result);
                }
                catch (IndexOutOfRangeException)
                {
                    return prev.Bind(state => ParseState.Throw($"Error: Unexpected EOF -- expected \"{c}\", but got EOF near \"{state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}\""));
                }
            }
            else return prev;
        }

        public static ParseState Parse(this ParseState prev, string s)
        {
            if (prev.State == ErrorState.Result)
            {
                ParseState result = prev;
                foreach (char c in s)
                {
                    result = result.Parse(c);
                }

                return result.Match(Result: _ => result,
                                    Throw: e =>
                                    {
                                        if (e.StartsWith("Error: Unexpected EOF")) return prev.Bind(state => ParseState.Throw($"Error: Unexpected EOF -- Expected \"{s}\", but got EOF near \"{state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}\""));
                                        else return prev.Bind(state => ParseState.Throw($"Error: Expected \"{s}\", but got \"{state.Item2[0]}\" near \"{state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}\""));
                                    });
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
                    string[] optionsDescriptions = options.Select(o => o.Item2).ToArray();
                    string description = "";
                    for (int i = 0; i < optionsDescriptions.Count(); i++)
                    {
                        if (i != optionsDescriptions.Count() - 1) description += optionsDescriptions[i] + ", ";
                        else description += "or " + optionsDescriptions[i];
                    }
                    return prev.Bind(state => ParseState.Throw($"Error: Expected {description} near \"{state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}\""));
                }
                else return prev;
            };
        }

        public static Tuple<Func<ParseState, ParseState>, string> NewOption(Func<ParseState, ParseState> ps, string description) => Tuple.Create(ps, description);

        public static ParseState ParseWhile(this ParseState prev, Func<char, bool> f, string description, bool log = true) =>
            prev.Bind(
                state =>
                {
                    Tuple<string, string, ImmutableList<Either<string, int>>> parsed = state;
                    if (!f(parsed.Item2[0])) return ParseState.Throw($"Error: expected {description}, got {parsed.Item2[0]} near {state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}");
                    string stringParsed = "";
                    foreach (char c in parsed.Item2)
                    {
                        if (f(c))
                        {
                            try
                            {
                                parsed = Tuple.Create(parsed.Item1 + parsed.Item2[0], string.Concat(parsed.Item2.Skip(1)), parsed.Item3);
                            }
                            catch (IndexOutOfRangeException)
                            {
                                return prev.Bind(state2 => ParseState.Throw($"Error: Unexpected EOF -- expected {description}, but got EOF near \"{state2.Item1.Substring(Math.Max(state2.Item1.Length - 10, 0))}\""));
                            }
                            stringParsed += c;
                        }
                        else break;
                    }
                    if (log) parsed = Tuple.Create(parsed.Item1, parsed.Item2, parsed.Item3.Add(Either<string, int>.Left(stringParsed)));
                    return ParseState.Result(parsed);
                });

        public static ParseState ParseOn(this ParseState prev, Func<char, bool> f, string description, bool log = true)
        {
            if (prev.State == ErrorState.Result)
            {
                try
                {
                    return (from state in prev
                            from result in f(state.Item2[0])
                                           ? ParseState.Return(Tuple.Create(
                                                state.Item1 + state.Item2[0],
                                                string.Concat(state.Item2.Skip(1)),
                                                log ? state.Item3.Add(Either<string, int>.Left(state.Item2[0].ToString())) : state.Item3))
                                           : ParseState.Throw($"Error: Expected {description}, but got {state.Item2} near {state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}")
                            select result);
                }
                catch (IndexOutOfRangeException)
                {
                    return prev.Bind(state => ParseState.Throw($"Error: Unexpected EOF -- expected {description}, but got EOF near \"{state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}\""));
                }
            }
            else return prev;
        }

        public static ParseState ParseRegex(this ParseState prev, string regex, string description, bool log = true)
        {
            if (prev.State == ErrorState.Result)
            {
                try
                {
                    return prev.Bind(state =>
                    {
                        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(state.Item2, "\\A(?:" + regex + ")");
                        if (match.Value == "") return ParseState.Throw($"Error: Expected {description} near {state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}");
                        else return ParseState.Result(Tuple.Create(
                                                state.Item1 + state.Item2.Substring(0, match.Length),
                                                string.Concat(state.Item2.Skip(match.Length)),
                                                log ? state.Item3.Add(Either<string, int>.Left(state.Item2[0].ToString())) : state.Item3));
                    });
                }
                catch (IndexOutOfRangeException)
                {
                    return prev.Bind(state => ParseState.Throw($"Error: Unexpected EOF -- expected {description}, but got EOF near \"{state.Item1.Substring(Math.Max(state.Item1.Length - 10, 0))}\""));
                }
            }
            else return prev;
        }

        public static ParseState Rule(this ParseState prev, int rulenum) => prev.Bind(state => ParseState.Return(
                                                                                             Tuple.Create(state.Item1,
                                                                                                          state.Item2,
                                                                                                          state.Item3.Add(Either<string, int>.Right(rulenum)))));

        public static NonTerminal Rule(this NonTerminal nt, int rulenum) => prev => prev.Parse(nt).Rule(rulenum);

        public static ParseState epsilon(ParseState text) => text.Rule(-1);

        public static ParseState EmptyParseState(string text) => ParseState.Return(Tuple.Create("", text, ImmutableList<Either<string, int>>.Empty));

        public static Tuple<string, int> Specifier(string s, int i) => Tuple.Create(s, i);

        public static AST ProcessAST(ImmutableList<AlgebraicTypes.Either<string, int>> log, ASTMap map)
        {
            Tree<TermSpecification> _ast = new Tree<TermSpecification>();
            List<int> position = new List<int>();
            bool first = true;
            foreach (Either<string, int> item in log.Reverse())
            {
                item.Match
                (
                    Right: nt =>
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
                        return Unit.Nil;
                    },
                    Left: v =>
                    {
                        _ast.Rightmost(ts => (ts.State == TermSpecificationState.Base) ||
                                             (ts.State == TermSpecificationState.Ignore))
                            .Match
                            (
                                Just: rightmost =>
                                {
                                    _ast.Navigate(rightmost).Children = new List<Tree<TermSpecification>>() { new Tree<TermSpecification>(TermSpecification.Terminal(v)) };
                                    return Unit.Nil;
                                },
                                Nothing: () => Unit.Nil
                            );
                        return Unit.Nil;
                    }

                );
            }
            return AST.FromTree(_ast, map);
        }

        public static void Initialise(this ASTMap map)
        {
            map.Add(Specifier(nameof(epsilon), -1), new List<TermSpecification> { TermSpecification.Terminal("") });
        }
    }
    
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
    // TermSpecification = Terminal string | NonTerminal int | Option int int | Base | Ignore
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
            public ImmutableList<int> Values { get; set; } = ImmutableList<int>.Empty;
            public OptionImpl(ImmutableList<int> vs)
            {
                Values = vs;
            }
        }
        private class BaseImpl
        {
            public BaseImpl()
            {
            }
        }
        private class IgnoreImpl
        {
            public IgnoreImpl()
            {
            }
        }
        public TermSpecificationState State { get; set; }
        private TerminalImpl TerminalField;
        private TerminalImpl TerminalValue { get { return TerminalField; } set { TerminalField = value; NonTerminalField = null; OptionField = null; BaseField = null; IgnoreField = null; State = TermSpecificationState.Terminal; } }
        private NonTerminalImpl NonTerminalField;
        private NonTerminalImpl NonTerminalValue { get { return NonTerminalField; } set { NonTerminalField = value; TerminalField = null; OptionField = null; BaseField = null; IgnoreField = null; State = TermSpecificationState.NonTerminal; } }
        private OptionImpl OptionField;
        private OptionImpl OptionValue { get { return OptionField; } set { OptionField = value; TerminalField = null; NonTerminalField = null; BaseField = null; IgnoreField = null; State = TermSpecificationState.Option; } }
        private BaseImpl BaseField;
        private BaseImpl BaseValue { get { return BaseField; } set { BaseField = value; TerminalField = null; NonTerminalField = null; OptionField = null; IgnoreField = null; State = TermSpecificationState.Base; } }
        private IgnoreImpl IgnoreField;
        private IgnoreImpl IgnoreValue { get { return IgnoreField; } set { IgnoreField = value; TerminalField = null; NonTerminalField = null; OptionField = null; BaseField = null; State = TermSpecificationState.Ignore; } }
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
        public static TermSpecification Option(params int[] values)
        {
            TermSpecification result = new TermSpecification();
            result.OptionValue = new OptionImpl(values.ToImmutableList());
            return result;
        }
        public static TermSpecification Base()
        {
            TermSpecification result = new TermSpecification();
            result.BaseValue = new BaseImpl();
            return result;
        }
        public static TermSpecification Ignore()
        {
            TermSpecification result = new TermSpecification();
            result.IgnoreValue = new IgnoreImpl();
            return result;
        }
        public T1 Match<T1>(Func<string, T1> Terminal, Func<int, T1> NonTerminal, Func<ImmutableList<int>, T1> Option, Func<T1> Base, Func<T1> Ignore)
        {
            switch (State)
            {
                case TermSpecificationState.Terminal: return Terminal(TerminalValue.Value1);
                case TermSpecificationState.NonTerminal: return NonTerminal(NonTerminalValue.Value1);
                case TermSpecificationState.Option: return Option(OptionValue.Values);
                case TermSpecificationState.Base: return Base();
                case TermSpecificationState.Ignore: return Ignore();
            }
            return default(T1);
        }
        public override string ToString() => this.Match(Terminal: t => "Terminal " + t,
                                                        NonTerminal: nt => "Nonterminal " + nt,
                                                        Option: os => "Option " + string.Join(" ", os.Select(o => o.ToString())),
                                                        Base: () => "Base",
                                                        Ignore: () => "Ignore");
    }
    public enum TermSpecificationState
    {
        Terminal, NonTerminal, Option, Base, Ignore
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
                            Base: () => { if (t.Children.Count == 1) return t.Children[0].Value.Match(Terminal: s => Term.Terminal(s),
                                                                                                      NonTerminal: _ => { throw new Exception(); },
                                                                                                      Option: _ => { throw new Exception(); },
                                                                                                      Base: () => { throw new Exception(); },
                                                                                                      Ignore: () => { throw new Exception(); });
                                          else throw new Exception(); },
                            Ignore: () => null // we will remove this node later in the function
                        );
            _ast.Children = _ast.Term.Match(Terminal: _ => new List<AST>(),
                NonTerminal: nt => nt == nameof(MParse.epsilon) ? new List<AST>() : t.Children.Select(child => FromTree(child, map)).ToList());
            _ast.RemoveAll(ast => ast == null);
            return _ast;
        }

        private static string GetNonTerminal(ASTMap map, int nt) => map.Keys.Where(s => s.Item2 == nt)
                                                                         .Select(s => s.Item1)
                                                                         .First();
    }
}
