using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlgebraicTypes;

using ParseState = AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>;
using NonTerminal = System.Func<AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>,
                                AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>>;
using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.TermSpecification>>;
using T = MParse.TermSpecification;

using static MParse.MParse;

namespace MParse
{
    static class Program
    {
        static void Main(string[] args)
        {
            ASTMap map = new ASTMap
            {
                [Specifier(nameof(Start), 0)] = new List<T> { T.NonTerminal(1), T.Terminal(";"), T.Option(0, -1) },
                [Specifier(nameof(Statement), 1)] = new List<T> { T.Option(2, 3, 4) },
                [Specifier(nameof(Increment), 2)] = new List<T> { T.NonTerminal(7), T.Terminal("++") },
                [Specifier(nameof(Decrement), 3)] = new List<T> { T.NonTerminal(7), T.Terminal("--") },
                [Specifier(nameof(Assignment), 4)] = new List<T> { T.Option(5, 6) },
                [Specifier(nameof(Assignment1), 5)] = new List<T> { T.NonTerminal(7), T.Terminal("="), T.NonTerminal(7) },
                [Specifier(nameof(Assignment1), 6)] = new List<T> { T.NonTerminal(7), T.Terminal("="), T.NonTerminal(10) },
                [Specifier(nameof(ID), 7)] = new List<T> { T.Base() },
                [Specifier(nameof(Literal), 8)] = new List<T> { T.Option(9, 10) },
                [Specifier(nameof(IntLiteral), 9)] = new List<T> { T.Base() },
                [Specifier(nameof(StringLiteral), 10)] = new List<T> { T.Base() }
            };
            map.Initialise();
            while (true)
            {
                MParse.DoParse(Start, Console.ReadLine(), map).Match
                (
                    Result: ast => { ast.PrintPretty("", true); return Unit.Nil; },
                    Throw: e => { Console.WriteLine(e); return Unit.Nil; }
                );
            }
        }

        public static void ClearLine() // Thanks to SomeNameNoFake from http://stackoverflow.com/questions/8946808/can-console-clear-be-used-to-only-clear-a-line-instead-of-whole-console
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, Console.CursorTop - (Console.WindowWidth >= Console.BufferWidth ? 1 : 0));
        }

        public static void PrintPretty(this AST t, string indent, bool last) // With thanks to Will from http://stackoverflow.com/questions/1649027/how-do-i-print-out-a-tree-structure
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
            Console.WriteLine(t.Term.ToString());
            for (int i = 0; i < t.Children.Count; i++)
                t.Children[i].PrintPretty(indent, i == t.Children.Count - 1);

        }

        static ParseState Start(ParseState text) => text.Parse(Statement).Parse(';').Parse(Option(NewOption(Start, "statement"), NewOption(epsilon, "epsilon"))).Rule(0);

        static NonTerminal Statement => Option(NewOption(Increment, "increment"), NewOption(Decrement, "decrement"), NewOption(Assignment, "assignment")).Rule(1);

        static ParseState Increment(ParseState text) => text.Parse(ID).Parse("++").Rule(2);

        static ParseState Decrement(ParseState text) => text.Parse(ID).Parse("--").Rule(3);

        static NonTerminal Assignment => Option(NewOption(Assignment1, "assignment"), NewOption(Assignment2, "assignment")).Rule(4);
        static ParseState Assignment1(ParseState text) => text.Parse(ID).Parse('=').Parse(ID).Rule(5);
        static ParseState Assignment2(ParseState text) => text.Parse(ID).Parse('=').Parse(Literal).Rule(6);

        static ParseState ID(ParseState text) => text.ParseRegex(@"[_a-zA-Z](?:[a-zA-Z0-9]*)", "identifier").Rule(7);

        static NonTerminal Literal => Option(NewOption(IntLiteral, "integer literal"), NewOption(StringLiteral, "string literal")).Rule(8);
        static ParseState IntLiteral(ParseState text) => text.ParseWhile(c => char.IsDigit(c), "digit").Rule(9);
        static ParseState StringLiteral(ParseState text) => text.ParseRegex(@""".*""", "string literal").Rule(10);
    }
}

