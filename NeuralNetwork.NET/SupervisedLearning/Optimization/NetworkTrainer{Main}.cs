﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Results;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Networks.Implementations;
using NeuralNetworkNET.Networks.Layers.Abstract;
using NeuralNetworkNET.Services;
using NeuralNetworkNET.SupervisedLearning.Algorithms.Info;
using NeuralNetworkNET.SupervisedLearning.Data;
using NeuralNetworkNET.SupervisedLearning.Parameters;
using NeuralNetworkNET.SupervisedLearning.Progress;
using NeuralNetworkNET.SupervisedLearning.Trackers;

namespace NeuralNetworkNET.SupervisedLearning.Optimization
{
    /// <summary>
    /// A static class that contains various network optimization algorithms
    /// </summary>
    internal static partial class NetworkTrainer
    {
        /// <summary>
        /// Trains the target <see cref="SequentialNetwork"/> with the given parameters and data
        /// </summary>
        /// <param name="network">The target <see cref="SequentialNetwork"/> to train</param>
        /// <param name="batches">The training baatches for the current session</param>
        /// <param name="epochs">The desired number of training epochs to run</param>
        /// <param name="dropout">Indicates the dropout probability for neurons in a <see cref="LayerType.FullyConnected"/> layer</param>
        /// <param name="algorithm">The info on the training algorithm to use</param>
        /// <param name="batchProgress">An optional callback to monitor the training progress (in terms of dataset completion)</param>
        /// <param name="trainingProgress">An optional progress callback to monitor progress on the training dataset (in terms of classification performance)</param>
        /// <param name="validationDataset">The optional <see cref="ValidationDataset"/> instance (used for early-stopping)</param>
        /// <param name="testDataset">The optional <see cref="TestDataset"/> instance (used to monitor the training progress)</param>
        /// <param name="token">The <see cref="CancellationToken"/> for the training session</param>
        [NotNull]
        public static TrainingSessionResult TrainNetwork(
            [NotNull] SequentialNetwork network, [NotNull] BatchesCollection batches,
            int epochs, float dropout,
            [NotNull] ITrainingAlgorithmInfo algorithm,
            [CanBeNull] IProgress<BatchProgress> batchProgress,
            [CanBeNull] IProgress<TrainingProgressEventArgs> trainingProgress,
            [CanBeNull] ValidationDataset validationDataset,
            [CanBeNull] TestDataset testDataset,
            CancellationToken token)
        {
            SharedEventsService.TrainingStarting.Raise();
            switch (algorithm)
            {
                /* =================
                 * Optimization
                 * =================
                 * The different optimization methods available are called here, and they will in turn call the base Optimize method, passing a WeightsUpdater
                 * delegate that implements their own optimization logic. The reason why these methods are calling one another (instead of passing a single delegate
                 * for each different optimizer) is that this way each optimizer implementation can keep local parameters allocated on the stack, if necessary, while
                 * that wouldn't be possible with a delegate. Moreover, this way they can handle their own cleanup for unmanaged resources (for temporary training data)
                 * without the need to pass another optional delegate just for that reason. */
                case StochasticGradientDescentInfo sgd:
                    return StochasticGradientDescent(network, batches, epochs, dropout, sgd.Eta, sgd.Lambda, batchProgress, trainingProgress, validationDataset, testDataset, token);
                case AdadeltaInfo adadelta:
                    return Adadelta(network, batches, epochs, dropout, adadelta.Rho, adadelta.Epsilon, adadelta.L2, batchProgress, trainingProgress, validationDataset, testDataset, token);
                case AdamInfo adam:
                    return Adam(network, batches, epochs, dropout, adam.Eta, adam.Beta1, adam.Beta2, adam.Epsilon, batchProgress, trainingProgress, validationDataset, testDataset, token);
                default:
                    throw new ArgumentException("The input training algorithm type is not supported");
            }
        }

        /// <summary>
        /// Trains the target <see cref="SequentialNetwork"/> using the input algorithm
        /// </summary>
        [NotNull]
        private static TrainingSessionResult Optimize(
            SequentialNetwork network,
            BatchesCollection miniBatches,
            int epochs, float dropout,
            [NotNull] WeightsUpdater updater,
            [CanBeNull] IProgress<BatchProgress> batchProgress,
            [CanBeNull] IProgress<TrainingProgressEventArgs> trainingProgress,
            [CanBeNull] ValidationDataset validationDataset,
            [CanBeNull] TestDataset testDataset,
            CancellationToken token)
        {
            // Setup
            DateTime startTime = DateTime.Now;
            List<DatasetEvaluationResult>
                validationReports = new List<DatasetEvaluationResult>(),
                testReports = new List<DatasetEvaluationResult>();
            TrainingSessionResult PrepareResult(TrainingStopReason reason, int loops)
            {
                return new TrainingSessionResult(reason, loops, DateTime.Now.Subtract(startTime).RoundToSeconds(), validationReports, testReports);
            }

            // Convergence manager for the validation dataset
            RelativeConvergence convergence = validationDataset == null
                ? null
                : new RelativeConvergence(validationDataset.Tolerance, validationDataset.EpochsInterval);

            // Optional batch monitor
            BatchProgressMonitor batchMonitor = batchProgress == null ? null : new BatchProgressMonitor(miniBatches.Count, batchProgress);

            // Create the training batches
            for (int i = 0; i < epochs; i++)
            {
                // Shuffle the training set
                miniBatches.CrossShuffle();

                // Gradient descent over the current batches
                for (int j = 0; j < miniBatches.BatchesCount; j++)
                {
                    if (token.IsCancellationRequested) return PrepareResult(TrainingStopReason.TrainingCanceled, i);
                    network.Backpropagate(miniBatches.Batches[j], dropout, updater);
                    batchMonitor?.NotifyCompletedBatch(miniBatches.Batches[j].X.GetLength(0));
                }
                batchMonitor?.Reset();

                // Check for overflows
                if (!Parallel.For(0, network._Layers.Length, (j, state) =>
                {
                    if (network._Layers[j] is WeightedLayerBase layer && !layer.ValidateWeights()) state.Break();
                }).IsCompleted) return PrepareResult(TrainingStopReason.NumericOverflow, i);

                // Check the training progress
                if (trainingProgress != null)
                {
                    (float cost, _, float accuracy) = network.Evaluate(miniBatches);
                    trainingProgress.Report(new TrainingProgressEventArgs(i + 1, cost, accuracy));
                }

                // Check the validation dataset
                if (convergence != null)
                {
                    (float cost, _, float accuracy) = network.Evaluate(validationDataset.Dataset);
                    validationReports.Add(new DatasetEvaluationResult(cost, accuracy));
                    convergence.Value = accuracy;
                    if (convergence.HasConverged) return PrepareResult(TrainingStopReason.EarlyStopping, i);
                }

                // Report progress if necessary
                if (testDataset != null)
                {
                    (float cost, _, float accuracy) = network.Evaluate(testDataset.Dataset);
                    testReports.Add(new DatasetEvaluationResult(cost, accuracy));
                    testDataset.ProgressCallback?.Report(new TrainingProgressEventArgs(i + 1, cost, accuracy));
                }
            }
            return PrepareResult(TrainingStopReason.EpochsCompleted, epochs);
        }
    }
}
