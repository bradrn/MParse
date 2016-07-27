using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CSFunc.Types;

namespace MParse.Lexer
{
    public class Lexer
    {
        public ImmutableList<KeyValuePair<string, int>> TokenSpecifications { get; private set; }
        public Lexer(params KeyValuePair<string, int>[] tokenSpecifications)
        {
            List<KeyValuePair<string, int>> _tokenSpecifications = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> tokenSpecification in tokenSpecifications)
            {
                _tokenSpecifications.Add(tokenSpecification);
            }
            TokenSpecifications = _tokenSpecifications.ToImmutableList();
        }
        public Error<ImmutableList<Token>, LexerError> Lex(string input, Func<int, int, ILocation> locator)
        {
            List<Token> tokens = new List<Token>();
            int i = 0;
            string cur = input;
            bool hasFoundMatch = false;
            while (cur != "")
            {
                hasFoundMatch = false;
                foreach (KeyValuePair<string, int> tokenSpecification in TokenSpecifications)
                {
                    Match m = Regex.Match(cur, @"\A" + tokenSpecification.Key);
                    if (m.Success)
                    {
                        tokens.Add(new Token(tokenSpecification.Value, m.Value, locator(m.Index, m.Length)));
                        cur = cur.Substring(m.Length);
                        i += m.Length;
                        hasFoundMatch = true;
                        break;
                    }
                }
                if (!hasFoundMatch) return Error<ImmutableList<Token>, LexerError>.Throw(new MParse.Lexer.LexerError(cur[0], locator(i, 0)));
            }
            return Error<ImmutableList<Token>, LexerError>.Result(tokens.ToImmutableList());
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

        public override string ToString()
        {
            return $"Token({Type}, {Value}, {Location.ToString()})";
        }
    }

    public interface ILocation { }

    public class TokenError
    {
        public ILocation Location { get; }
        public TokenError(ILocation location)
        {
            Location = location;
        }
    }

    public class LexerError : TokenError
    {
        public char Next { get; }
        public LexerError(char next, ILocation location) : base(location)
        {
            Next = next;
        }
        public string ToString(bool putPositionAtFront = true)
        {
            string result = "";
            if (putPositionAtFront) result += $"{Location.ToString()} ";
            result += $"Error: Unexpected character {Next}";
            if (!putPositionAtFront) result += $"at {Location.ToString()}";
            return result;
        }
    }
}
