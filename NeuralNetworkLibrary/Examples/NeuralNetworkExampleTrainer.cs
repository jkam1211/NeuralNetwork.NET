﻿using System;
using NeuralNetworkLibrary.Examples.BoardGames.Enums;
using NeuralNetworkLibrary.Examples.BoardGames.Implementations;
using NeuralNetworkLibrary.GeneticAlgorithm;

namespace NeuralNetworkLibrary.Examples
{
    /// <summary>
    /// A sample class that contains some ready to use fitness functions to train neural network to play various games
    /// </summary>
    public static class NeuralNetworkExampleTrainer
    {
        /// <summary>
        /// Gets a fitness function that lets the neural networks learn how to play TicTacToe
        /// </summary>
        public static NeuralNetworkGeneticAlgorithmProvider.FitnessDelegate TicTacToeFitnessFunction { get; } = (uid, forward) =>
        {
            // Initialize the score, the random provider and start looping
            double score = 0;
            Random random = new Random(1);
            for (int i = 0; i < 300; i++)
            {
                // Switch to true random values for the last 100 games
                if (i == 200) random = new Random(uid);
                TicTacToe match = new TicTacToe(i % 2 == 0, random);
                double t = 0;
                while (true)
                {
                    // Check if the match is finished
                    if (match.AvailableMoves == 0)
                    {
                        CrossesGameResult rr = match.CheckMatchResult();
                        if (rr == CrossesGameResult.PlayerVictory) score += 10;
                        else if (rr == CrossesGameResult.OpponentVictory) score -= 50;
                        break;
                    }

                    // Serialize and get the next move
                    double[,] serialized = match.Serialize();
                    double[,] move = forward(serialized);
                    int position = 0;
                    double max = double.MinValue;
                    for (int j = 0; j < move.GetLength(1); j++)
                    {
                        if (move[0, j] > max)
                        {
                            max = move[0, j];
                            position = j;
                        }
                    }
                    int x = position / 3, y = position % 3;

                    // Try to move and check the result
                    if (!match.Move(x, y, true)) score -= 2;
                    else
                    {
                        t += 0.5;
                        score += t;
                    }
                    CrossesGameResult r = match.CheckMatchResult();
                    if (r == CrossesGameResult.PlayerVictory)
                    {
                        score += 10;
                        break;
                    }
                    if (r == 0 && match.AvailableMoves == 0) break;

                    // Opponent turn
                    match.MoveOpponent();
                    r = match.CheckMatchResult();
                    if (r == CrossesGameResult.OpponentVictory)
                    {
                        score -= 50;
                        break;
                    }
                    if (r == 0 && match.AvailableMoves == 0) break;
                }
            }
            return score;
        };

        /// <summary>
        /// Gets a fitness function that lets the neural networks learn how to play 2048
        /// </summary>
        public static NeuralNetworkGeneticAlgorithmProvider.FitnessDelegate _2048FitnessFunction { get; } = (uid, forward) =>
        {
            // Initialize the random provider and start the loop
            Random random = new Random(uid);
            double top = 0;
            for (int i = 0; i < 4; i++)
            {
                _2048 match = new _2048(random);
                while (true)
                {
                    // Serialize and get the next move
                    double[,] serialized = match.Serialize();
                    double[,] move = forward(serialized);
                    int position = 0;
                    double max = double.MinValue;
                    for (int j = 0; j < move.GetLength(1); j++)
                    {
                        if (move[0, j] > max)
                        {
                            max = move[0, j];
                            position = j;
                        }
                    }
                    Direction dir;
                    switch (position)
                    {
                        case 0: dir = Direction.Up; break;
                        case 1: dir = Direction.Down; break;
                        case 2: dir = Direction.Left; break;
                        case 3: dir = Direction.Right; break;
                        default: throw new InvalidOperationException();
                    }

                    // Try to move and check the result
                    if (!match.Move(dir, false))
                    {
                        Direction? d = match.NextAvailableMove;
                        if (d == null) break;
                        match.Move(d.Value, false);
                    }

                    if (match.NextAvailableMove == null) break;
                }
                if (match.Score > top) top = match.Score;
            }
            return top;
        };
    }
}
