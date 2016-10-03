using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CSFunc.Types;
using MParse.Parser;
using MParse.Lexer;

// Using statements required for MParse.Parser
using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>>;
using AST = MParse.Parser.Tree<MParse.Parser.Term>;
using TSpec = System.Collections.Generic.KeyValuePair<string, int>;

namespace Tests
{
    [TestClass]
    public class ParserTest
    {
        [TestMethod]
        public void Parse_SingleToken_Success()
        {
            NonTerminal nt = text => text.Parse(0).Rule(0);
            Error<AST, ParseError> result = Parser.DoParse(nt, ImmutableList.Create(new Token(0, "token", new SampleLocation())));
            Assert.IsTrue(result.State == ErrorState.Result);
            Assert.IsTrue(result.Match(Result: ast => ast.Value.NonTerminalOrDefault(NonTerminal: _nt => _nt == 0,
                                                                                     Default: false),
                                       Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[0].Value.TerminalOrDefault(Terminal: tok => tok.Value == "token" && tok.Type == 0,
                                                                                              Default: false),
                                       Throw: perr => false));
        }

        [TestMethod]
        public void Parse_SingleToken_Failure()
        {
            NonTerminal nt = text => text.Parse(0).Rule(0);
            Error<AST, ParseError> result = Parser.DoParse(nt, ImmutableList.Create(new Token(1, "token", new SampleLocation())));
            Assert.IsTrue(result.State == ErrorState.Throw);
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => (perr.Expected.State == ParseError.ExpectedValueState.Token)));
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => (perr.Expected.TokenOrDefault(Token: tok => tok == 0,
                                                                                                          Default: false))));
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => (perr.Got.State == ParseError.GotValueState.Token)));
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => (perr.Got.TokenOrDefault(Token: tok => tok.Type == 1 && tok.Value == "token",
                                                                                                     Default: false))));
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => perr.Location is SampleLocation));  // Can't say `perr.Location == new SampleLocation()
                                                                                                                // because equality is by reference
        }

        [TestMethod]
        public void Parse_MultipleTokens_Success()
        {
            NonTerminal nt = text => text.Parse(0).Parse(1).Rule(0);
            Error<AST, ParseError> result = Parser.DoParse(nt, ImmutableList.Create(new Token(0, "token0", new SampleLocation()),
                                                                                    new Token(1, "token1", new SampleLocation())));
            Assert.IsTrue(result.State == ErrorState.Result);
            Assert.IsTrue(result.Match(Result: ast => ast.Value.NonTerminalOrDefault(NonTerminal: _nt => _nt == 0,
                                                                                     Default: false),
                                       Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[0].Value.TerminalOrDefault(Terminal: tok => tok.Value == "token0" && tok.Type == 0,
                                                                                              Default: false),
                                       Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[1].Value.TerminalOrDefault(Terminal: tok => tok.Value == "token1" && tok.Type == 1,
                                                                                              Default: false),
                                       Throw: perr => false));
        }

        [TestMethod]
        public void Parse_MultipleTokens_Failure()
        {
            NonTerminal nt = text => text.Parse(0).Parse(1).Rule(0);
            Error<AST, ParseError> result = Parser.DoParse(nt, ImmutableList.Create(new Token(0, "token0-0", new SampleLocation()),
                                                                                    new Token(0, "token0-1", new SampleLocation())));
            Assert.IsTrue(result.State == ErrorState.Throw);
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => perr.Expected.State == ParseError.ExpectedValueState.Token));
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => perr.Expected.TokenOrDefault(Token: tok => tok == 1,
                                                                                                         Default: false)));
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => perr.Got.State == ParseError.GotValueState.Token));
            Assert.IsTrue(result.Match(Result: ast => false, Throw: perr => perr.Got.TokenOrDefault(Token: tok => tok.Type == 0 && tok.Value == "token0-1",
                                                                                                    Default: false)));
        }


        [TestMethod]
        public void Parse_NonTerminal_Success()
        {
            NonTerminal nt2 = text => text.Parse(0).Rule(1);
            NonTerminal nt1 = text => text.Parse(nt2).Parse(nt2).Rule(0);
            Error<AST, ParseError> result = Parser.DoParse(nt1, ImmutableList.Create(new Token(0, "token0", new SampleLocation()),
                                                                                     new Token(0, "token1", new SampleLocation())));
            Assert.IsTrue(result.State == ErrorState.Result);
            Assert.IsTrue(result.Match(Result: ast => ast.Value.NonTerminalOrDefault(NonTerminal: nt => nt == 0,
                                                                                     Default: false),
                                       Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[0].Value.State == TermState.NonTerminal, Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[0].Value.NonTerminalOrDefault(NonTerminal: nt => nt == 1,
                                                                                                 Default: false),
                                       Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[1].Value.NonTerminalOrDefault(NonTerminal: nt => nt == 1,
                                                                                                 Default: false),
                                       Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[0].Children[0].Value.TerminalOrDefault(Terminal: tok => tok.Type == 0 & tok.Value == "token0",
                                                                                                          Default: false),
                                       Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[1].Children[0].Value.TerminalOrDefault(Terminal: tok => tok.Type == 0 & tok.Value == "token1",
                                                                                                          Default: false),
                                       Throw: perr => false));

        }

        class SampleLocation : ILocation { }
    }
}
