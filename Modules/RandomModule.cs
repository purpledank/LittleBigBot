﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord.WebSocket;
using LittleBigBot.Attributes;
using LittleBigBot.Entities;
using LittleBigBot.Results;
using Qmmands;

/**
 * Commands in this module
 *
 * - OTP
 * - Roll
 * - Choose
 */
namespace LittleBigBot.Modules
{
    [Name("Random")]
    [Description("Commands that involve a computerised RNG calculator.")]
    public class RandomModule : LittleBigBotModuleBase
    {
        public enum DiceExpressionOptions
        {
            None,
            SimplifyStringValue
        }

        public Random Random { get; set; }

        [Command("OTP", "Ship")]
        [Description("Ships two random members of this server/DM.")]
        public Task<BaseResult> Command_OtpAsync(
            [ParameterArrayOptional] [Name("Blocked Users")] [Description("Users who will be skipped in the random selection.")]
            params SocketUser[] users)
        {
            if (Context.IsPrivate) return Ok($":heart: I ship **you** x **me**, {Context.Invoker.Username}! :heart:");

            var arr = users.ToList();
            var guildUsers = Context.Guild.Users.Where(a => arr.All(b => b.Id != a.Id) && !a.IsBot);
            var socketGuildUsers = guildUsers as SocketGuildUser[] ?? guildUsers.ToArray();

            if (socketGuildUsers.Length < 2) return Ok("This guild is too small, or you have ignored too many people!");

            SocketGuildUser GetRandomUser()
            {
                var user = socketGuildUsers.ElementAt(Random.Next(0, socketGuildUsers.Length));
                return user;
            }

            var member1 = GetRandomUser();
            var member2 = GetRandomUser();

            while (member1 == member2) member1 = GetRandomUser();

            return Ok(
                $":heart: I ship **{member1.Nickname ?? member1.Username}** x **{member2.Nickname ?? member2.Username}**! :heart:");
        }

        [Command("Roll", "Dice", "RollDice")]
        [Remarks("This command also supports complex dice types, like `d20+d18+4`.")]
        [Description("Rolls a dice of the supplied size.")]
        public Task<BaseResult> Command_DiceRollAsync(
            [Name("Dice")] [Description("The dice configuration to use. It can be simple, like `6`, or complex, like `d20+d18+4`.")]
            string dice = "6", [Name("Number of Dice")] [Description("The number of dice to roll.")]
            int numberOfDice = 1)
        {
            if (numberOfDice < 1) return BadRequest("You must ask me to roll at least one die!");

            if (numberOfDice > 100) return BadRequest("Sorry! No more than 100 dice rolls at once, please!");

            if (!dice.Contains("d" /* No dice */) && int.TryParse(dice, out var diceParsed))
            {
                if (diceParsed < 1) return BadRequest("Your dice roll must be 1 or above!");

                if (numberOfDice == 1)
                    return Ok($"I rolled **{Random.Next(1, diceParsed)}** on a **{dice}**-sided die.");

                return Ok(string.Join("\n", Enumerable.Range(1, numberOfDice).Select(a => $"- **Die {a}:** {Random.Next(1, diceParsed)}")));
            }

            try
            {
                return Ok(string.Join("\n", Enumerable.Range(1, numberOfDice)
                    .Select(a => $"- **Die {a}:** {DiceExpression.Evaluate(dice)}")));
            }
            catch (ArgumentException)
            {
                return BadRequest("Invalid dice!");
            }
        }

        [Command("Is")]
        [Description("Determines if a user has a specific attribute.")]
        public Task<BaseResult> IsUserAsync(SocketUser target, [Remainder] string attribute)
        {
            var @is = Random.Next(0, 2) == 1;
            attribute = attribute.Replace("?", ".");
            var username = target is SocketGuildUser u ? u.Nickname ?? u.Username : target.Username;

            return Ok(
                $"{(@is ? "Yes" : "No")}, {username} is {(@is ? "" : "not ")}{attribute}{(attribute.EndsWith(".") ? "" : ".")}");
        }

        [Command("Does")]
        [Description("Determines if a user does something, or has an attribute.")]
        public Task<BaseResult> DoesUserAsync(SocketUser target, [Remainder] string attribute)
        {
            var does = Random.Next(0, 2) == 1;
            attribute = attribute.Replace("?", ".");
            var username = target is SocketGuildUser u ? u.Nickname ?? u.Username : target.Username;

            return Ok(
                $"{(does ? "Yes" : "No")}, {username} does {(does ? "" : "not ")}{attribute}{(attribute.EndsWith(".") ? "" : ".")}");
        }

        [Command("Choose", "Pick")]
        [Description("Picks an option out of a list.")]
        public Task<BaseResult> Command_PickOptionAsync(
            [Name("Options")] [Description("The options to choose from.")]
            params string[] options)
        {
            if (options.Length == 0) return BadRequest("You have to give me options to pick from!");

            var roll = Random.Next(0, options.Length);
            return Ok($"I choose **{options[roll]}**.");
        }

        public class DiceExpression
        {
            private static readonly Regex NumberToken = new Regex("^[0-9]+$");
            private static readonly Regex DiceRollToken = new Regex("^([0-9]*)d([0-9]+|%)$");

            public static readonly DiceExpression Zero = new DiceExpression("0");

            private readonly List<KeyValuePair<int, IDiceExpressionNode>> _nodes =
                new List<KeyValuePair<int, IDiceExpressionNode>>();

            public DiceExpression(string expression, DiceExpressionOptions options = DiceExpressionOptions.None)
            {
                // A well-formed dice expression's tokens will be either +, -, an integer, or XdY.
                var tokens = expression.Replace("+", " + ").Replace("-", " - ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Blank dice expressions end up being DiceExpression.Zero.
                if (!tokens.Any()) tokens = new[] {"0"};

                // Since we parse tokens in operator-then-operand pairs, make sure the first token is an operand.
                if (tokens[0] != "+" && tokens[0] != "-") tokens = new[] {"+"}.Concat(tokens).ToArray();

                // This is a precondition for the below parsing loop to make any sense.
                if (tokens.Length % 2 != 0)
                    throw new ArgumentException(
                        "The given dice expression was not in an expected format: even after normalization, it contained an odd number of tokens.");

                // Parse operator-then-operand pairs into nodes.
                for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex += 2)
                {
                    var token = tokens[tokenIndex];
                    var nextToken = tokens[tokenIndex + 1];

                    if (token != "+" && token != "-")
                        throw new ArgumentException("The given dice expression was not in an expected format.");

                    var multiplier = token == "+" ? +1 : -1;

                    if (NumberToken.IsMatch(nextToken))
                    {
                        _nodes.Add(new KeyValuePair<int, IDiceExpressionNode>(multiplier,
                            new NumberNode(int.Parse(nextToken))));
                    }
                    else if (DiceRollToken.IsMatch(nextToken))
                    {
                        var match = DiceRollToken.Match(nextToken);
                        var numberOfDice = match.Groups[1].Value == string.Empty ? 1 : int.Parse(match.Groups[1].Value);
                        var diceType = match.Groups[2].Value == "%" ? 100 : int.Parse(match.Groups[2].Value);
                        _nodes.Add(new KeyValuePair<int, IDiceExpressionNode>(multiplier,
                            new DiceRollNode(numberOfDice, diceType)));
                    }
                    else
                    {
                        throw new ArgumentException(
                            "The given dice expression was not in an expected format: the non-operand token was neither a number nor a dice-roll expression.");
                    }
                }

                // Sort the nodes in an aesthetically-pleasing fashion.
                var diceRollNodes = _nodes.Where(pair => pair.Value.GetType() == typeof(DiceRollNode))
                    .OrderByDescending(node => node.Key)
                    .ThenByDescending(node => ((DiceRollNode) node.Value).DiceType)
                    .ThenByDescending(node => ((DiceRollNode) node.Value).NumberOfDice).ToList();
                var numberNodes = _nodes.Where(pair => pair.Value.GetType() == typeof(NumberNode))
                    .OrderByDescending(node => node.Key)
                    .ThenByDescending(node => node.Value.Evaluate());

                // If desired, merge all number nodes together, and merge dice nodes of the same type together.
                if (options == DiceExpressionOptions.SimplifyStringValue)
                {
                    var number = numberNodes.Sum(pair => pair.Key * pair.Value.Evaluate());
                    var diceTypes = diceRollNodes.Select(node => ((DiceRollNode) node.Value).DiceType).Distinct();
                    var normalizedDiceRollNodes = from type in diceTypes
                        let numDiceOfThisType = diceRollNodes
                            .Where(node => ((DiceRollNode) node.Value).DiceType == type).Sum(node =>
                                node.Key * ((DiceRollNode) node.Value).NumberOfDice)
                        where numDiceOfThisType != 0
                        let multiplicand = numDiceOfThisType > 0 ? +1 : -1
                        let absNumDice = Math.Abs(numDiceOfThisType)
                        orderby multiplicand descending, type descending
                        select new KeyValuePair<int, IDiceExpressionNode>(multiplicand,
                            new DiceRollNode(absNumDice, type));

                    _nodes = (number == 0
                            ? normalizedDiceRollNodes
                            : normalizedDiceRollNodes.Concat(new[]
                            {
                                new KeyValuePair<int, IDiceExpressionNode>(number > 0 ? +1 : -1, new NumberNode(number))
                            }))
                        .ToList();
                }
                // Otherwise, just put the dice-roll nodes first, then the number nodes.
                else
                {
                    _nodes = diceRollNodes.Concat(numberNodes).ToList();
                }
            }

            public static int Evaluate(string expression, DiceExpressionOptions options = DiceExpressionOptions.None)
            {
                return new DiceExpression(expression, options).Evaluate();
            }

            public override string ToString()
            {
                var result = (_nodes[0].Key == -1 ? "-" : string.Empty) + _nodes[0].Value;
                foreach (var pair in _nodes.Skip(1))
                {
                    result += pair.Key == +1 ? " + " : " − "; // NOTE: unicode minus sign, not hyphen-minus '-'.
                    result += pair.Value.ToString();
                }

                return result;
            }

            public int Evaluate()
            {
                var result = 0;
                foreach (var pair in _nodes) result += pair.Key * pair.Value.Evaluate();

                return result;
            }

            public decimal GetCalculatedAverage()
            {
                decimal result = 0;
                foreach (var pair in _nodes) result += pair.Key * pair.Value.GetCalculatedAverage();

                return result;
            }

            private interface IDiceExpressionNode
            {
                int Evaluate();
                decimal GetCalculatedAverage();
            }

            private class NumberNode : IDiceExpressionNode
            {
                private readonly int _theNumber;

                public NumberNode(int theNumber)
                {
                    _theNumber = theNumber;
                }

                public int Evaluate()
                {
                    return _theNumber;
                }

                public decimal GetCalculatedAverage()
                {
                    return _theNumber;
                }

                public override string ToString()
                {
                    return _theNumber.ToString();
                }
            }

            private class DiceRollNode : IDiceExpressionNode
            {
                private static readonly Random Roller = new Random();

                public DiceRollNode(int numberOfDice, int diceType)
                {
                    NumberOfDice = numberOfDice;
                    DiceType = diceType;
                }

                public int NumberOfDice { get; }

                public int DiceType { get; }

                public int Evaluate()
                {
                    var total = 0;
                    for (var i = 0; i < NumberOfDice; ++i) total += Roller.Next(1, DiceType + 1);

                    return total;
                }

                public decimal GetCalculatedAverage()
                {
                    return NumberOfDice * ((DiceType + 1.0m) / 2.0m);
                }

                public override string ToString()
                {
                    return string.Format("{0}d{1}", NumberOfDice, DiceType);
                }
            }
        }
    }
}