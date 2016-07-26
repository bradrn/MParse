using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSFunc.Types;
using MParse.Lexer;

using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<CSFunc.Types.Either<MParse.Lexer.Token, int>>>, MParse.Parser.ParseError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<CSFunc.Types.Either<MParse.Lexer.Token, int>>>, MParse.Parser.ParseError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<CSFunc.Types.Either<MParse.Lexer.Token, int>>>, MParse.Parser.ParseError>>;
using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.Parser.TermSpecification>>;
using AST = MParse.Parser.Tree<CSFunc.Types.Either<MParse.Lexer.Token, int>>;
using T = MParse.Parser.TermSpecification;

using static MParse.Parser.Parser;

namespace MParse.Parser
{
    static class Program
    {
        const int SEMICOLON = 0;
        const int INCREMENT = 1;
        const int DECREMENT = 2;
        const int EQUALS = 3;
        const int ID = 4;
        const int INTEGER_LITERAL = 5;
        const int STRING_LITERAL = 6;

        static void Main(string[] args)
        {
            ASTMap map = new ASTMap
            {
                [Specifier(nameof(Start), 0)] = new List<T> { T.NonTerminal(1), T.Terminal(SEMICOLON), T.Option(0, -1) },
                [Specifier(nameof(Statement), 1)] = new List<T> { T.Option(2, 3, 4) },
                [Specifier(nameof(Increment), 2)] = new List<T> { T.Terminal(ID), T.Terminal(INCREMENT) },
                [Specifier(nameof(Decrement), 3)] = new List<T> { T.Terminal(ID), T.Terminal(DECREMENT) },
                [Specifier(nameof(Assignment), 4)] = new List<T> { T.Option(5, 6) },
                [Specifier(nameof(Assignment1), 5)] = new List<T> { T.Terminal(ID), T.Terminal(EQUALS), T.Terminal(ID) },
                [Specifier(nameof(Assignment1), 6)] = new List<T> { T.Terminal(ID), T.Terminal(EQUALS), T.NonTerminal(7) },
                [Specifier(nameof(Literal), 7)] = new List<T> { T.Option(8, 9) },
                [Specifier(nameof(IntLiteral), 8)] = new List<T> { T.Terminal(INTEGER_LITERAL) },
                [Specifier(nameof(StringLiteral), 9)] = new List<T> { T.Terminal(STRING_LITERAL) }
            }.Initialise();
            while (true)
            {
                PrintPretty(DoParse(Start, new List<Token> { Token(ID,        "abcd", new Line(0)),
                                                             Token(INCREMENT, "++",   new Line(4)),
                                                             Token(SEMICOLON, ";",    new Line(6)),
                                                             Token(ID,        "e",    new Line(7)),
                                                             Token(EQUALS,    "=",    new Line(8)),
                                                             Token(INTEGER_LITERAL, "1", new Line(9)),
                                                             Token(SEMICOLON, ";",    new Line(10))}.ToImmutableList(), map).Match
                           (
                               Result: ast => ast,
                               Throw: terr =>
                               {
                                   Console.WriteLine(terr.ToString(new Dictionary<int, string>
                                   {
                                       [SEMICOLON] = "semicolon",
                                       [INCREMENT] = "increment (++)",
                                       [DECREMENT] = "decrement (--)",
                                       [EQUALS] = "assignment operator (=)",
                                       [ID] = "identifier",
                                       [INTEGER_LITERAL] = "integer",
                                       [STRING_LITERAL] = "string"
                                   }));
                                   return new AST(Either<Token, int>.Left(Token(-1, "Error", new Line(-1))));
                               }
                           ), "", true);
                Console.ReadLine();
            }
        }

        public static void ClearLine() // Thanks to SomeNameNoFake from http://stackoverflow.com/questions/8946808/can-console-clear-be-used-to-only-clear-a-line-instead-of-whole-console
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, Console.CursorTop - (Console.WindowWidth >= Console.BufferWidth ? 1 : 0));
        }

        public static void PrintPretty<T>(this Tree<T> t, string indent, bool last) // With thanks to Will from http://stackoverflow.com/questions/1649027/how-do-i-print-out-a-tree-structure
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

        static ParseState Start(ParseState text)
        {
            ParseState parsed = text.Parse(Statement).Parse(SEMICOLON);
            ParseState parsed2 = parsed.Parse(Start);
            if (parsed2.State == ErrorState.Throw)
            {
                if (!parsed2.Match(Result: _ => false, Throw: terr => terr.Expected.Match(EOF: () => false,
                                                                                          Token: tok => tok == SEMICOLON,
                                                                                          Option: o => false)))
                {
                    parsed2 = parsed.Parse(epsilon);
                }
            }
            return parsed2.Rule(0);
        }

        static NonTerminal Statement => Option(Option(Increment, "increment"), Option(Decrement, "decrement"), Option(Assignment, "assignment")).Rule(1);

        static ParseState Increment(ParseState text) => text.Parse(ID).Parse(INCREMENT).Rule(2);

        static ParseState Decrement(ParseState text) => text.Parse(ID).Parse(DECREMENT).Rule(3);

        static NonTerminal Assignment => Option(Option(Assignment1, "assignment"), Option(Assignment2, "assignment")).Rule(4);
        static ParseState Assignment1(ParseState text) => text.Parse(ID).Parse(EQUALS).Parse(ID).Rule(5);
        static ParseState Assignment2(ParseState text) => text.Parse(ID).Parse(EQUALS).Parse(Literal).Rule(6);

        static NonTerminal Literal => Option(Option(IntLiteral, "integer literal"), Option(StringLiteral, "string literal")).Rule(7);
        static ParseState IntLiteral(ParseState text) => text.Parse(INTEGER_LITERAL).Rule(8);
        static ParseState StringLiteral(ParseState text) => text.Parse(STRING_LITERAL).Rule(9);
    }

    public class Line : ILocation
    {
        public int Loc { get; set; }
        public Line(int loc) { Loc = loc; }
        public override string ToString()
        {
            return $"({Loc})";
        }
    }
}

