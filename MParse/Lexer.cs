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
        public Error<ImmutableList<Token>, TokenError> Lex(string input, Func<int, int, ILocation> locator)
        {
            List<Token> tokens = new List<Token>();
            string cur = input;
            while (cur != "")
            {
                foreach (KeyValuePair<string, int> tokenSpecification in TokenSpecifications)
                {
                    Match m = Regex.Match(cur, @"\A" + tokenSpecification.Key);
                    if (m.Success)
                    {
                        tokens.Add(new Token(tokenSpecification.Value, m.Value, locator(m.Index, m.Length)));
                        cur = cur.Substring(m.Length - 1);
                    }
                }
            }
            return Error<ImmutableList<Token>, TokenError>.Result(tokens.ToImmutableList());
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
}
