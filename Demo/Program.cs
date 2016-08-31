using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSFunc.Types;
using MParse.Parser;
using MParse.Lexer;

using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>>;
using AST = MParse.Parser.Tree<MParse.Parser.Term>;
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
            Lexer l = new Lexer(new TSpec(@";", SEMICOLON),
                    new TSpec(@"\+\+", INCREMENT),
                    new TSpec(@"--", DECREMENT),
                    new TSpec(@"=", EQUALS),
                    new TSpec(@"[a-zA-Z_][a-zA-Z0-9_]*", ID),
                    new TSpec(@"[0-9]+", INTEGER_LITERAL),
                    new TSpec(@""".*""", STRING_LITERAL),
                    new TSpec(@"\s|(//.*)$", -1));
            new eNFA(new Dictionary<int, List<KeyValuePair<Maybe<char>, int>>>
            {
                [1] = new List<KeyValuePair<Maybe<char>, int>> { new KeyValuePair<Maybe<char>, int>(Maybe<char>.Just('a'), 2), new KeyValuePair<Maybe<char>, int>(Maybe<char>.Nothing(), 3) },
                [2] = new List<KeyValuePair<Maybe<char>, int>> { new KeyValuePair<Maybe<char>, int>(Maybe<char>.Just('a'), 4), new KeyValuePair<Maybe<char>, int>(Maybe<char>.Nothing(), 3) },
                [3] = new List<KeyValuePair<Maybe<char>, int>> { new KeyValuePair<Maybe<char>, int>(Maybe<char>.Just('b'), 2), new KeyValuePair<Maybe<char>, int>(Maybe<char>.Nothing(), 1),
                                                                                                                               new KeyValuePair<Maybe<char>, int>(Maybe<char>.Nothing(), 5) },
                [4] = new List<KeyValuePair<Maybe<char>, int>>(),
                [5] = new List<KeyValuePair<Maybe<char>, int>>()
            }, new List<int> { 4, 5 }, 1)
            .Close()
            .StateTable
            .ToList()
            .ForEach(kvp =>
            {
                Console.WriteLine(kvp.Key + " | " + kvp.Value.Select(_kvp => _kvp.Key + " -> " + _kvp.Value));
            });
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
                        DoParse(Start, ts).Match
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
                        ).PrintPretty();
                        return Unit.Nil;
                    }
                );
            }
        }

        static ParseState Start(ParseState text)
        {
            if (text.State == ErrorState.Result)
            {
                ParseState parsed = text;
                while (true)
                {
                    ParseState _parsed = parsed.Parse(Statement);
                    if (_parsed.State == ErrorState.Throw)
                    {
                        if (_parsed.Match(Result: state => false, Throw: state => state.Expected.Match(EOF: () => false,
                                                                                                       Token: tok => tok == SEMICOLON,
                                                                                                       Option: os => false)))
                        {
                            return _parsed;
                        }
                        else return parsed.Rule(0);
                    }
                    else parsed = _parsed;
                }
            }
            else return text.Rule(0);
        }

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

