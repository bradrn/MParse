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
        }
        public Error<ImmutableList<Token>, LexerError> Lex(string input, Func<int, int, ILocation> locator)
        {
            throw new NotImplementedException();
        }
    }

    public class DFA
    {
        public Dictionary<int, Dictionary<char, int>> StateTable { get; set; }
        public int StartingState { get; set; }
        public List<int> AcceptingStates { get; set; }
        public DFA(Dictionary<int, Dictionary<char, int>> stateTable, List<int> acceptingStates, int startingState = 0)
        {
            StateTable = stateTable;
            StartingState = startingState;
            AcceptingStates = acceptingStates;
        }
    }

    public class NFA
    {
        public Dictionary<int, List<KeyValuePair<char, int>>> StateTable { get; set; }
        public List<int> StartingStates { get; set; }
        public List<int> AcceptingStates { get; set; }
        public NFA(Dictionary<int, List<KeyValuePair<char, int>>> stateTable, List<int> acceptingStates, List<int> startingStates)
        {
            StateTable = stateTable;
            StartingStates = startingStates;
            AcceptingStates = acceptingStates;
        }
        public DFA ToDFA()
        {
            /* A DFA is created from an NFA in two steps:
             *
             *   (1) Construct a DFA whose each of whose states is composite,
             *       namely a list of NFA states.
             *
             *   (2) Replace composite states (List of int) by simple states
             *       (int).
             * (https://www.itu.dk/people/sestoft/gcsharp/GNfaToDfa.cs)
             */

            Func<List<int>, List<int>, bool> equalUnordered = (l1, l2) => Enumerable.SequenceEqual(l1.OrderBy(i => i), l2.OrderBy(i => i));

            // STEP 1
            Dictionary<List<int>, Dictionary<char, List<int>>> compositeDFA = new Dictionary<List<int>, Dictionary<char, List<int>>>();
            Queue<List<int>> compositeStatesNotDone = new Queue<List<int>>(); // Keeps track of composite states that have not yet been added to the DFA

            Dictionary<char, List<int>> startingStateClosure = CloseState(StartingStates);
            compositeDFA.Add(StartingStates, startingStateClosure);
            foreach (KeyValuePair<char, List<int>> kvp in startingStateClosure)
                if (!compositeStatesNotDone.Any(l => equalUnordered(l, kvp.Value))) compositeStatesNotDone.Enqueue(kvp.Value);
            
            while (compositeStatesNotDone.Count != 0)
            {
                List<int> curCompositeState = compositeStatesNotDone.Dequeue();
                Dictionary<char, List<int>> curCompositeStateClosure = CloseState(curCompositeState);
                compositeDFA.Add(curCompositeState, curCompositeStateClosure);
                foreach (KeyValuePair<char, List<int>> kvp in curCompositeStateClosure)
                    if (!compositeStatesNotDone.Any(l => equalUnordered(l, kvp.Value)) &&   // If the state has not already been added to compositeStatesNotDone
                        !compositeDFA.Keys.Any(l => equalUnordered(l, kvp.Value)))          // and if it is not already in compositeDFA
                        compositeStatesNotDone.Enqueue(kvp.Value);
            }

            // STEP 2
            Dictionary<List<int>, int> mappings = new Dictionary<List<int>, int>();
            int curState = 0;
            foreach (KeyValuePair<List<int>, Dictionary<char, List<int>>> compositeTransitions in compositeDFA)
            {
                if (!mappings.Any(mapping => equalUnordered(mapping.Key, compositeTransitions.Key)))
                {
                    mappings.Add(compositeTransitions.Key, curState);
                    curState++;
                }
            }
            int startState = mappings.First(kvp => equalUnordered(StartingStates, kvp.Key)).Value;

            DFA dfa = new DFA(new Dictionary<int, Dictionary<char, int>>(), new List<int>(), startState);
            Func<List<int>, int> getNum = l => mappings.First(kvp => equalUnordered(kvp.Key, l)).Value;
            foreach (KeyValuePair<List<int>, Dictionary<char, List<int>>> compositeTransitions in compositeDFA)
            {
                dfa.StateTable.Add(getNum(compositeTransitions.Key), compositeTransitions.Value.ToDictionary(kvp => kvp.Key, kvp => getNum(kvp.Value)));
                if (compositeTransitions.Value.Count == 1 && compositeTransitions.Key.Contains(StartingState)) dfa.StartingState = getNum(compositeTransitions.Key);
                if (compositeTransitions.Key.Intersect(AcceptingStates).Any()) dfa.AcceptingStates.Add(getNum(compositeTransitions.Key));
            }
            return dfa;
        }
        private Dictionary<char, List<int>> CloseState(int state)
        {
            Dictionary<char, List<int>> combinedTransitions = new Dictionary<char, List<int>>();
            foreach (KeyValuePair<char, int> transition in StateTable[state])
            {
                if (combinedTransitions.Select(kvp => kvp.Key).Contains(transition.Key))
                {
                    if (!combinedTransitions.First(kvp => kvp.Key == transition.Key).Value.Contains(transition.Value))
                        combinedTransitions.First(kvp => kvp.Key == transition.Key).Value.Add(transition.Value);
                }
                else combinedTransitions.Add(transition.Key, new List<int> { transition.Value });
            }
            return combinedTransitions;
        }
        private Dictionary<char, List<int>> CloseState(List<int> combinedState)
        {
            Dictionary<char, List<int>> combinedTransitions = new Dictionary<char, List<int>>();
            foreach (int state in combinedState)
            {
                // Merge CloseState(state) with combinedTransitions
                CloseState(state).ToList().ForEach(kvp =>
                {
                    if (combinedTransitions.ContainsKey(kvp.Key)) combinedTransitions.First(_kvp => _kvp.Key == kvp.Key).Value.AddRange(kvp.Value);
                    else                                          combinedTransitions.Add(kvp.Key, kvp.Value);
                });
                combinedTransitions = combinedTransitions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Distinct().ToList()); // Remove duplicates in values
            }
            return combinedTransitions;
        }
    }

    public class eNFA
    {
        public Dictionary<int, List<KeyValuePair<Maybe<char>, int>>> StateTable { get; set; }
        public List<int> StartingStates { get; set; }
        public List<int> AcceptingStates { get; set; }
        public eNFA(Dictionary<int, List<KeyValuePair<Maybe<char>, int>>> stateTable, List<int> acceptingStates, List<int> startingStates)
        {
            StateTable = stateTable;
            StartingStates = startingStates;
            AcceptingStates = acceptingStates;
        }
        public NFA Close()
        {
            NFA nfa = new NFA(new Dictionary<int, List<KeyValuePair<char, int>>>(), this.AcceptingStates, this.StartingStates);
            foreach (KeyValuePair<int, List<KeyValuePair<Maybe<char>, int>>> state in StateTable)
            {
                List<int> closure = CloseState(state.Key, new List<int> { state.Key }.ToImmutableList());
                List<KeyValuePair<char, int>> transitions = new List<KeyValuePair<char, int>>();
                foreach (int _state in closure)
                {
                    transitions.AddRange(StateTable[_state].Where(kvp => kvp.Key.State == MaybeState.Just)
                                                           .Select(kvp =>
                                                                new KeyValuePair<char, int>(
                                                                    kvp.Key.Match(Just: c => c,
                                                                                  Nothing: () => { throw new Exception(); }),
                                                                    kvp.Value)));
                }
                nfa.StateTable.Add(state.Key, transitions);
            }
            return nfa;
        }
        private List<int> CloseState(int state, ImmutableList<int> alreadyVisitedStates)
        {
            List<int> epsilonReachableStates = (from kvp in StateTable[state]
                                                where kvp.Key.State == MaybeState.Nothing
                                                where !alreadyVisitedStates.Contains(kvp.Value)
                                                select kvp.Value).ToList();
            return epsilonReachableStates.SelectMany(s => CloseState(s,
                alreadyVisitedStates.AddRange(epsilonReachableStates))).ToImmutableList().Add(state).ToList();
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
