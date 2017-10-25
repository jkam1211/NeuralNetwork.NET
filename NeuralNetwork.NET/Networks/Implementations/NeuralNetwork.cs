﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NeuralNetworkNET.Helpers;
using NeuralNetworkNET.Networks.PublicAPIs;
using Newtonsoft.Json;

namespace NeuralNetworkNET.Networks.Implementations
{
    /// <summary>
    /// A complete and fully connected neural network with an arbitrary number of hidden layers
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal class NeuralNetwork : INeuralNetwork
    {
        #region Public parameters

        /// <inheritdoc/>
        [JsonProperty(nameof(InputLayerSize), Required = Required.Always)]
        public int InputLayerSize => Weights[0].GetLength(0);

        /// <inheritdoc/>
        [JsonProperty(nameof(OutputLayerSize), Required = Required.Always)]
        public int OutputLayerSize => Weights[Weights.Count - 1].GetLength(1);

        private int[] _HiddenLayers;

        /// <inheritdoc/>
        [JsonProperty(nameof(HiddenLayers), Required = Required.Always)]
        public IReadOnlyList<int> HiddenLayers => _HiddenLayers ?? (_HiddenLayers = Weights.Take(Weights.Count - 1).Select(w => w.GetLength(1)).ToArray());

        #endregion

        #region Local fields

        /// <summary>
        /// The list of weight matrices for the network
        /// </summary>
        [NotNull, ItemNotNull]
        [JsonProperty(nameof(Weights), Required = Required.Always)]
        protected readonly IReadOnlyList<double[,]> Weights;

        /// <summary>
        /// The precalculated list of transposed weight matrices to use inthe gradient function
        /// </summary>
        /// <remarks>The first item is always null (to save space), as it isn't needed to calculate the gradient</remarks>
        [NotNull, ItemCanBeNull]
        protected readonly double[][,] TransposedWeights;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes a new instance with the given parameters
        /// </summary>
        /// <param name="weights">The weights in all the network layers</param>
        internal NeuralNetwork([NotNull] IReadOnlyList<double[,]> weights)
        {
            // Input check
            if (weights.Count == 0) throw new ArgumentOutOfRangeException(nameof(weights), "The weights must have a length at least equal to 1");
            for (int i = 0; i < weights.Count; i++)
            {
                if (i > 0 && weights[i - 1].GetLength(1) != weights[i].GetLength(0))
                    throw new ArgumentOutOfRangeException(nameof(weights), "Some weight matrix doesn't have the right size");
            }

            // Parameters setup
            Weights = weights;
            TransposedWeights = new double[weights.Count][,];
        }

        /// <summary>
        /// Creates a new random instance with the given number of neurons in each layer
        /// </summary>
        /// <param name="neurons">The number of neurons from the input to the output layer</param>
        [NotNull]
        internal static NeuralNetwork NewRandom([NotNull] params int[] neurons)
        {
            if (neurons.Length < 2) throw new ArgumentOutOfRangeException(nameof(neurons), "The network must have at least two layers");
            Random random = new Random();
            double[][,] weights = new double[neurons.Length - 1][,];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = random.NextSigmoidMatrix(neurons[i], neurons[i + 1]);
            }
            return new NeuralNetwork(weights);
        }

        #endregion

        #region Single processing

        /// <inheritdoc/>
        public double[] Forward(double[] x) => Forward(x.ToMatrix()).Flatten();

        /// <inheritdoc/>
        public double CalculateCost(double[] x, double[] y) => CalculateCost(x.ToMatrix(), y.ToMatrix());

        /// <summary>
        /// Calculates the gradient of the cost function with respect to the individual weights and biases
        /// </summary>
        /// <param name="x">The input data</param>
        /// <param name="y">The expected result</param>
        [PublicAPI]
        [Pure, NotNull]
        [CollectionAccess(CollectionAccessType.Read)]
        internal double[] ComputeGradient([NotNull] double[] x, [NotNull] double[] y) => ComputeGradient(x.ToMatrix(), y.ToMatrix());

        #endregion

        #region Batch processing

        /// <inheritdoc/>
        public virtual double[,] Forward(double[,] x)
        {
            double[,] a0 = x;
            for (int i = 0; i < Weights.Count; i++)
            {
                a0 = MatrixServiceProvider.MultiplyAndActivation(a0, Weights[i]); // A(l) = sigm(W(l) * A(l - 1))
            }
            return a0; // At least one weight matrix, so a0 != x
        }

        /// <inheritdoc/>
        public double CalculateCost(double[,] input, double[,] y)
        {
            // Forward the input
            double[,] yHat = Forward(input);

            // Calculate the cost (half the squared difference)
            return MatrixServiceProvider.HalfSquaredDifference(yHat, y);
        }

        /// <summary>
        /// Calculates the gradient of the cost function with respect to the individual weights and biases
        /// </summary>
        /// <param name="x">The input data</param>
        /// <param name="y">The expected results</param>
        [PublicAPI]
        [Pure, NotNull]
        [CollectionAccess(CollectionAccessType.Read)]
        internal virtual double[] ComputeGradient([NotNull] double[,] x, [NotNull] double[,] y)
        {
            // Feedforward
            int steps = Weights.Count;  // Number of forward hops through the network
            double[][,]
                zList = new double[steps][,],
                aList = new double[steps][,];
            double[,] a0 = x;
            for (int i = 0; i < Weights.Count; i++)
            {
                // Save the intermediate steps to be able to reuse them later
                double[,] zi = MatrixServiceProvider.Multiply(a0, Weights[i]);
                zList[i] = zi;
                aList[i] = a0 = MatrixServiceProvider.Activation(zi);
            }

            /* ============================
             * Calculate delta(L) in place
             * ============================
             * Perform the sigmoid prime of zL, the activity on the last layer
             * Calculate the gradient of C with respect to a, so (yHat - y)
             * Compute d(L), the Hadamard product of the gradient and the sigmoid prime for L */
            double[,] dL = aList[aList.Length - 1];
            MatrixServiceProvider.InPlaceSubtractAndHadamardProductWithActivationPrime(dL, y, zList[zList.Length - 1]);

            // Backpropagation
            double[][,] deltas = new double[steps][,];      // One additional delta for each hop, delta(L) has already been calculated
            deltas[steps - 1] = dL;                         // Store the delta(L) in the last position
            for (int l = Weights.Count - 2; l >= 0; l--)    // Loop for l = L - 1, L - 2, ..., 2
            {
                // Precompute  W(l + 1) * delta(l + 1)
                double[,]
                    transposed = TransposedWeights[l + 1] ?? (TransposedWeights[l + 1] = Weights[l + 1].Transpose()), // Calculate W[l + 1]T if needed
                    dleft = MatrixServiceProvider.Multiply(deltas[l + 1], transposed),
                    dl = zList[l]; // Local reference on the delta to calculate in place

                /* ============================
                 * Calculate delta(l) in place
                 * ============================
                 * Perform the sigmoid prime of z(l), the activity on the previous layer
                 * Compute d(l), the Hadamard product of z'(l) and W(l + 1) * delta(l + 1) */
                MatrixServiceProvider.InPlaceActivationPrimeAndHadamardProduct(dl, dleft);
                deltas[l] = dl;
            }

            // Compute the gradient
            double[] gradient = new double[Weights.Sum(w => w.Length)]; // One gradient item for each weight
            int position = 0;
            for (int i = 0; i < Weights.Count; i++)
            {
                // Store the target delta
                double[,] di = deltas[i];

                // Compute dJdw(l)
                double[,] dJdw = i == 0
                    ? MatrixServiceProvider.TransposeAndMultiply(x, di)             // dJdW1, transposed input * first delta
                    : MatrixServiceProvider.TransposeAndMultiply(aList[i - 1], di); // dJdWi, previous activation transposed * current delta

                // Populate the gradient vector
                int bytes = sizeof(double) * dJdw.Length;
                Buffer.BlockCopy(dJdw, 0, gradient, position, bytes);
                position += bytes;
            }
            return gradient;
        }

        #endregion

        #region Tools

        /// <summary>
        /// Deserializes a neural network from the input weights and parameters
        /// </summary>
        /// <param name="data">The data representing the weights of the network</param>
        /// <param name="neurons">The number of nodes in each network layer</param>
        [PublicAPI]
        [Pure, NotNull]
        internal static NeuralNetwork Deserialize([NotNull] double[] data, [NotNull] params int[] neurons)
        {
            // Checks
            if (neurons.Length < 2) throw new ArgumentException("The network must have at least 2 layers");

            // Parse the input data
            int depth = neurons.Length - 1;
            double[][,] weights = new double[depth][,];
            int position = 0;
            for (int i = 0; i < depth; i++)
            {
                // Unpack the current weights
                double[,] wi = new double[neurons[i], neurons[i + 1]];
                int bytes = sizeof(double) * wi.Length;
                Buffer.BlockCopy(data, position, wi, 0, bytes);
                position += bytes;
                weights[i] = wi;
            }
            if (position / sizeof(double) != data.Length) throw new InvalidOperationException("Invalid network requested size");

            // Create the new network to use
            return new NeuralNetwork(weights);
        }

        /// <summary>
        /// Serializes the current network into a binary representation
        /// </summary>
        /// <returns>A <see cref="double"/> array containing all the weights of the network</returns>
        [PublicAPI]
        [Pure]
        internal virtual double[] Serialize()
        {
            // Allocate the output array
            int length = Weights.Sum(layer => layer.Length);
            double[] weights = new double[length];
            int position = 0;
            for (int i = 0; i < Weights.Count; i++)
            {
                // Populate the return array with the weights and biases for each layer
                int bytes = sizeof(double) * Weights[i].Length;
                Buffer.BlockCopy(Weights[i], 0, weights, position, bytes);
                position += bytes;
            }
            return weights;
        }

        /// <inheritdoc/>
        public String SerializeAsJSON() => JsonConvert.SerializeObject(this, Formatting.Indented);

        // Creates a new instance from another network with the same structure
        [Pure, NotNull]
        internal virtual NeuralNetwork Crossover([NotNull] NeuralNetwork other, [NotNull] Random random)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual bool Equals(INeuralNetwork other)
        {
            // Compare general features
            if (other.GetType() == typeof(NeuralNetwork) &&
                other.InputLayerSize == InputLayerSize &&
                other.OutputLayerSize == OutputLayerSize &&
                other.HiddenLayers.SequenceEqual(HiddenLayers))
            {
                // Compare each weight and bias value
                NeuralNetwork network = (NeuralNetwork)other;
                for (int i = 0; i < Weights.Count; i++)
                    if (!network.Weights[i].ContentEquals(Weights[i])) return false;
                return true;
            }
            return false;
        }

        #endregion
    }
}
