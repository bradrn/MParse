using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSFunc.Types;
using MParse.Parser;
using MParse.Lexer;

using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Parser.Term>>, MParse.Parser.ParseError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Parser.Term>>, MParse.Parser.ParseError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Parser.Term>>, MParse.Parser.ParseError>>;
using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.Parser.TermSpecification>>;
using AST = MParse.Parser.Tree<MParse.Parser.Term>;
using T = MParse.Parser.TermSpecification;
using TSpec = System.Collections.Generic.KeyValuePair<string, int>;

using static MParse.Parser.Parser;

namespace Demo
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
                [Specifier(nameof(Start), 0)] = new List<T> { T.Loop(1) },
                [Specifier(nameof(Statement), 1)] = new List<T> { T.Option(2, 3, 4), T.Terminal(SEMICOLON) },
                [Specifier(nameof(Increment), 2)] = new List<T> { T.Terminal(ID), T.Terminal(INCREMENT) },
                [Specifier(nameof(Decrement), 3)] = new List<T> { T.Terminal(ID), T.Terminal(DECREMENT) },
                [Specifier(nameof(Assignment), 4)] = new List<T> { T.Option(5, 6) },
                [Specifier(nameof(Assignment1), 5)] = new List<T> { T.Terminal(ID), T.Terminal(EQUALS), T.Terminal(ID) },
                [Specifier(nameof(Assignment1), 6)] = new List<T> { T.Terminal(ID), T.Terminal(EQUALS), T.NonTerminal(7) },
                [Specifier(nameof(Literal), 7)] = new List<T> { T.Option(8, 9) },
                [Specifier(nameof(IntLiteral), 8)] = new List<T> { T.Terminal(INTEGER_LITERAL) },
                [Specifier(nameof(StringLiteral), 9)] = new List<T> { T.Terminal(STRING_LITERAL) }
            }.Initialise();
            Lexer l = new Lexer(new TSpec(@";", SEMICOLON),
                    new TSpec(@"\+\+", INCREMENT),
                    new TSpec(@"--", DECREMENT),
                    new TSpec(@"=", EQUALS),
                    new TSpec(@"[a-zA-Z_][a-zA-Z0-9_]*", ID),
                    new TSpec(@"[0-9]+", INTEGER_LITERAL),
                    new TSpec(@""".*""", STRING_LITERAL),
                    new TSpec(@"\s|(//.*)$", -1));
            while (true)
            {
                string input = "";
                while (true) { string read = Console.ReadLine(); if (read == "") { break; } input += read + Environment.NewLine; }
                Error<ImmutableList<Token>, LexerError> toks = l.Lex(input, (s, len) => new Line(s));
                toks = toks.Map(ts => ts.Where(tok => tok.Type != -1).ToImmutableList());
                toks.Match
                (
                    Throw: terr => { Console.WriteLine(terr.ToString()); return Unit.Nil; },
                    Result: ts =>
                    {
                        PrintPretty(DoParse(Start, ts, map).Match
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
                                           return new AST(Term.Terminal(Token(-1, "Error", new Line(-1))));
                                       }
                                   ), "", true);
                        return Unit.Nil;
                    }
                );
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

        static ParseState Start(ParseState text) => text.Loop(Statement).Rule(0);

        static ParseState Statement(ParseState text) => text.Option(Option(Increment, "increment"), Option(Decrement, "decrement"), Option(Assignment, "assignment")).Parse(SEMICOLON).Rule(1);

        static ParseState Increment(ParseState text) => text.Parse(ID).Parse(INCREMENT).Rule(2);

        static ParseState Decrement(ParseState text) => text.Parse(ID).Parse(DECREMENT).Rule(3);

        static ParseState Assignment(ParseState text) => text.Option(Option(Assignment1, "assignment"), Option(Assignment2, "assignment")).Rule(4);
        static ParseState Assignment1(ParseState text) => text.Parse(ID).Parse(EQUALS).Parse(ID).Rule(5);
        static ParseState Assignment2(ParseState text) => text.Parse(ID).Parse(EQUALS).Parse(Literal).Rule(6);

        static ParseState Literal(ParseState text) => text.Option(Option(IntLiteral, "integer literal"), Option(StringLiteral, "string literal")).Rule(7);
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

