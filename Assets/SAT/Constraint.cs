using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Assets.SAT
{
    /// <summary>
    /// Represents a constraint in a problem, i.e. a literalion of literals
    /// </summary>
    [DebuggerDisplay("{" + nameof(Text) + "}")]
    public class Constraint
    {
        /// <summary>
        /// The literals in the constraint
        /// </summary>
        public readonly Literal[] Literals;

        public readonly int MinTrueLiterals;
        public readonly int MaxTrueLiterals;

        /// <summary>
        /// Unique integer within the Problem assigned to this constraint.
        /// Used for indexing into TrueLiteralCounts array.  So the
        /// count of literals for constraint c is TrueLiteralCounts[c.Index].
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// Returns a randomly chosen literal from the constraint.
        /// </summary>
        public Literal RandomLiteral => Literals.RandomElement();

        /// <summary>
        /// Make a new constraint containing the specified literals
        /// </summary>
        /// <param name="literals">Literals to include in the constraint</param>
        /// <param name="index">Position within the problem's constraint table of this constraint</param>
        public Constraint(Literal[] literals, int min, int max, int index)
        {
            Literals = literals;
            MinTrueLiterals = min;
            MaxTrueLiterals = max;
            Index = index;

            foreach (var d in Literals)
                if (d.IsPositive)
                    d.Proposition.PositiveConstraints.Add(this);
                else
                    d.Proposition.NegativeConstraints.Add(this);

            // Reconstruct the source text of the constraint
            // We do this rather than keeping the original text
            // to help debug the parsing process.  If the parser
            // gets it wrong, we'll see what it produced.
            var b = new StringBuilder();

            if (min != 1 || max != int.MaxValue)
            {
                if (min == max)
                    b.Append($"{min} of ");
                else if (min == 0 && max != int.MaxValue)
                    b.Append($"at most {max} of ");
                else if (max == int.MaxValue)
                    b.Append($"at least {min} of ");
                else
                    b.Append($"between {min} and {max} of ");
            }

            var firstOne = true;
            foreach (var d in Literals)
            {
                if (firstOne)
                    firstOne = false;
                else
                    b.Append(" | ");
                b.Append(d);
            }

            Text = b.ToString();
        }

        #region Parsing

        private static readonly Regex OfPattern = new Regex(@"^ *(\d+) of (.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex AtLeastPattern = new Regex(@"^ *at least (\d+) of (.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex AtMostPattern = new Regex(@"^ *at most (\d+) of (.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex BetweenPattern = new Regex(@"^ *between (\d+) and (\d+) of (.+)$", RegexOptions.IgnoreCase);

        /// <summary>
        /// A constraint containing the literals specified in the expression
        /// </summary>
        /// <param name="expression">A string representing the constraint, e.g. "a | !b | c"</param>
        /// <param name="problem"></param>
        public static Constraint FromExpression(string expression, Problem problem)
        {
            bool TryMatch(Regex r, out Match match)
            {
                match = r.Match(expression);
                return match.Success;
            }
            
            int min = 1;
            int max = int.MaxValue;
            var literals = expression;
            Match m = null;

            if (TryMatch(OfPattern, out m))
            {
                max = min = int.Parse(m.Groups[1].Value);
                literals = m.Groups[2].Value;
            } else if (TryMatch(AtLeastPattern, out m))
            {
                min = int.Parse(m.Groups[1].Value);
                literals = m.Groups[2].Value;
            }
            else if (TryMatch(AtMostPattern, out m))
            {
                max = int.Parse(m.Groups[1].Value);
                min = 0;
                literals = m.Groups[2].Value;
            }
            else if (TryMatch(BetweenPattern, out m))
            {
                min = int.Parse(m.Groups[1].Value);
                max = int.Parse(m.Groups[2].Value);
                literals = m.Groups[3].Value;
            }

            return new Constraint(ParseLiterals(literals, problem), min, max, problem.Constraints.Count);
        }

        
        /// <summary>
        /// Returns the Literal objects corresponding to the text representation of a constraint
        /// </summary>
        /// <param name="expression">The text for the constraint, e.g. "a | !b | c"</param>
        /// <param name="problem">The problem this constraint is a part of</param>
        /// <returns></returns>
        private static Literal[] ParseLiterals(string expression, Problem problem) 
            => expression.Split('|')
                .Select(subexpression => new Literal(subexpression.Trim(), problem))
                .ToArray();
        #endregion

        /// <summary>
        /// Textual representation of this constraint, e.g. "a | !b | c".
        /// </summary>
        public readonly string Text;

        public override string ToString() => Text;
    }
}
