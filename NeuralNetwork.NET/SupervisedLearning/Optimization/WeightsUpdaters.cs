﻿using System;
using JetBrains.Annotations;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Networks.Implementations;
using NeuralNetworkNET.Networks.Layers.Abstract;
using NeuralNetworkNET.SupervisedLearning.Algorithms.Info;

namespace NeuralNetworkNET.SupervisedLearning.Optimization
{
    /// <summary>
    /// A static class that produces <see cref="WeightsUpdater"/> instances for the available optimization methods
    /// </summary>
    internal static class WeightsUpdaters
    {
        /// <summary>
        /// Creates a stochastic gradient descent optimizer
        /// </summary>
        /// <param name="info">The optimizer parameters</param>
        [Pure, NotNull]
        public static WeightsUpdater StochasticGradientDescent([NotNull] StochasticGradientDescentInfo info)
        {
            float 
                eta = info.Eta,
                lambda = info.Lambda;
            unsafe void Minimize(int i, in Tensor dJdw, in Tensor dJdb, int samples, WeightedLayerBase layer)
            {
                // Tweak the weights
                float
                    alpha = eta / samples,
                    l2Factor = eta * lambda / samples;
                fixed (float* pw = layer.Weights)
                {
                    float* pdj = dJdw;
                    int w = layer.Weights.Length;
                    for (int x = 0; x < w; x++)
                        pw[x] -= l2Factor * pw[x] + alpha * pdj[x];
                }

                // Tweak the biases of the lth layer
                fixed (float* pb = layer.Biases)
                {
                    float* pdj = dJdb;
                    int w = layer.Biases.Length;
                    for (int x = 0; x < w; x++)
                        pb[x] -= alpha * pdj[x];
                }
            }

            return Minimize;
        }

        /// <summary>
        /// Creates an Adadelta optimizer
        /// </summary>
        /// <param name="info">The optimizer parameters</param>
        /// <param name="network">The target network to optimize</param>
        [Pure, NotNull]
        public static WeightsUpdater Adadelta([NotNull] AdadeltaInfo info, [NotNull] SequentialNetwork network)
        {
            // Initialize Adadelta parameters
            float
                rho = info.Rho,
                epsilon = info.Epsilon,
                l2 = info.L2;
            float[][]
                egSquaredW = new float[network.WeightedLayersIndexes.Length][],
                eDeltaxSquaredW = new float[network.WeightedLayersIndexes.Length][],
                egSquaredB = new float[network.WeightedLayersIndexes.Length][],
                eDeltaxSquaredB = new float[network.WeightedLayersIndexes.Length][];
            for (int i = 0; i < network.WeightedLayersIndexes.Length; i++)
            {
                WeightedLayerBase layer = network._Layers[network.WeightedLayersIndexes[i]].To<NetworkLayerBase, WeightedLayerBase>();
                egSquaredW[i] = new float[layer.Weights.Length];
                eDeltaxSquaredW[i] = new float[layer.Weights.Length];
                egSquaredB[i] = new float[layer.Biases.Length];
                eDeltaxSquaredB[i] = new float[layer.Biases.Length];
            }

            // Adadelta update for weights and biases
            unsafe void Minimize(int i, in Tensor dJdw, in Tensor dJdb, int samples, WeightedLayerBase layer)
            {
                fixed (float* pw = layer.Weights, egSqrt = egSquaredW[i], eDSqrtx = eDeltaxSquaredW[i])
                {
                    float* pdj = dJdw;
                    int w = layer.Weights.Length;
                    for (int x = 0; x < w; x++)
                    {
                        float gt = pdj[x];
                        egSqrt[x] = rho * egSqrt[x] + (1 - rho) * gt * gt;
                        float
                            rmsDx_1 = (float)Math.Sqrt(eDSqrtx[x] + epsilon),
                            rmsGt = (float)Math.Sqrt(egSqrt[x] + epsilon),
                            dx = -(rmsDx_1 / rmsGt) * gt;
                        eDSqrtx[x] = rho * eDSqrtx[x] + (1 - rho) * dx * dx;
                        pw[x] += dx - l2 * pw[x];
                    }
                }

                // Tweak the biases of the lth layer
                fixed (float* pb = layer.Biases, egSqrt = egSquaredB[i], eDSqrtb = eDeltaxSquaredB[i])
                {
                    float* pdj = dJdb;
                    int w = layer.Biases.Length;
                    for (int b = 0; b < w; b++)
                    {
                        float gt = pdj[b];
                        egSqrt[b] = rho * egSqrt[b] + (1 - rho) * gt * gt;
                        float
                            rmsDx_1 = (float)Math.Sqrt(eDSqrtb[b] + epsilon),
                            rmsGt = (float)Math.Sqrt(egSqrt[b] + epsilon),
                            db = -(rmsDx_1 / rmsGt) * gt;
                        eDSqrtb[b] = rho * eDSqrtb[b] + (1 - rho) * db * db;
                        pb[b] += db - l2 * pb[b];
                    }
                }
            }

            return Minimize;
        }

        /// <summary>
        /// Creates an Adam optimizer
        /// </summary>
        /// <param name="info">The optimizer parameters</param>
        /// <param name="network">The target network to optimize</param>
        [Pure, NotNull]
        public static WeightsUpdater Adam([NotNull] AdamInfo info, [NotNull] SequentialNetwork network)
        {
            // Initialize Adam parameters
            float
                eta = info.Eta,
                beta1 = info.Beta1,
                beta2 = info.Beta2,
                epsilon = info.Epsilon;
            float[][]
                mW = new float[network.WeightedLayersIndexes.Length][],
                vW = new float[network.WeightedLayersIndexes.Length][],
                mB = new float[network.WeightedLayersIndexes.Length][],
                vB = new float[network.WeightedLayersIndexes.Length][];
            float[]
                beta1t = new float[network.WeightedLayersIndexes.Length],
                beta2t = new float[network.WeightedLayersIndexes.Length];
            for (int i = 0; i < network.WeightedLayersIndexes.Length; i++)
            {
                WeightedLayerBase layer = network._Layers[network.WeightedLayersIndexes[i]].To<NetworkLayerBase, WeightedLayerBase>();
                mW[i] = new float[layer.Weights.Length];
                vW[i] = new float[layer.Weights.Length];
                mB[i] = new float[layer.Biases.Length];
                vB[i] = new float[layer.Biases.Length];
                beta1t[i] = beta1;
                beta2t[i] = beta2;
            }

            // Adadelta update for weights and biases
            unsafe void Minimize(int i, in Tensor dJdw, in Tensor dJdb, int samples, WeightedLayerBase layer)
            {
                // Alpha at timestep t
                float alphat = eta * (float)Math.Sqrt(1 - beta2t[i]) / (1 - beta1t[i]);
                beta1t[i] *= beta1;
                beta2t[i] *= beta2;

                // Weights
                fixed (float* pw = layer.Weights, pm = mW[i], pv = vW[i])
                {
                    float* pdJ = dJdw;
                    int w = layer.Weights.Length;
                    for (int x = 0; x < w; x++)
                    {
                        float pdJi = pdJ[x];
                        pm[x] = pm[x] * beta1 + (1 - beta1) * pdJi;
                        pv[x] = pv[x] * beta2 + (1 - beta2) * pdJi * pdJi;
                        pw[x] -= alphat * pm[x] / ((float)Math.Sqrt(pv[x]) + epsilon);
                    }
                }

                // Biases
                fixed (float* pb = layer.Biases, pm = mB[i], pv = vB[i])
                {
                    float* pdJ = dJdb;
                    int w = layer.Biases.Length;
                    for (int b = 0; b < w; b++)
                    {
                        float pdJi = pdJ[b];
                        pm[b] = pm[b] * beta1 + (1 - beta1) * pdJi;
                        pv[b] = pv[b] * beta2 + (1 - beta2) * pdJi * pdJi;
                        pb[b] -= alphat * pm[b] / ((float)Math.Sqrt(pv[b]) + epsilon);
                    }
                }
            }

            return Minimize;
        }

        /// <summary>
        /// Creates an AdaMax optimizer
        /// </summary>
        /// <param name="info">The optimizer parameters</param>
        /// <param name="network">The target network to optimize</param>
        [Pure, NotNull]
        public static WeightsUpdater AdaMax([NotNull] AdaMaxInfo info, [NotNull] SequentialNetwork network)
        {
            // Initialize Adadelta parameters
            float
                eta = info.Eta,
                beta1 = info.Beta1,
                beta2 = info.Beta2;
            float[][]
                mW = new float[network.WeightedLayersIndexes.Length][],
                uW = new float[network.WeightedLayersIndexes.Length][],
                mB = new float[network.WeightedLayersIndexes.Length][],
                uB = new float[network.WeightedLayersIndexes.Length][];
            float[] beta1t = new float[network.WeightedLayersIndexes.Length];
            for (int i = 0; i < network.WeightedLayersIndexes.Length; i++)
            {
                WeightedLayerBase layer = network._Layers[network.WeightedLayersIndexes[i]].To<NetworkLayerBase, WeightedLayerBase>();
                mW[i] = new float[layer.Weights.Length];
                uW[i] = new float[layer.Weights.Length];
                mB[i] = new float[layer.Biases.Length];
                uB[i] = new float[layer.Biases.Length];
                beta1t[i] = beta1;
            }

            // Adadelta update for weights and biases
            unsafe void Minimize(int i, in Tensor dJdw, in Tensor dJdb, int samples, WeightedLayerBase layer)
            {
                // Alpha at timestep t
                float b1t = beta1t[i];
                beta1t[i] *= beta1;

                // Weights
                fixed (float* pw = layer.Weights, pm = mW[i], pu = uW[i])
                {
                    float* pdJ = dJdw;
                    int w = layer.Weights.Length;
                    for (int x = 0; x < w; x++)
                    {
                        float pdJi = pdJ[x];
                        pm[x] = beta1 * pm[x] + (1 - beta1) * pdJi;
                        pu[x] = (beta2 * pu[x]).Max(pdJi.Abs());
                        pw[x] -= eta / (1 - b1t) * pm[x] / pu[x];
                    }
                }

                // Biases
                fixed (float* pb = layer.Biases, pm = mB[i], pu = uB[i])
                {
                    float* pdJ = dJdb;
                    int w = layer.Biases.Length;
                    for (int b = 0; b < w; b++)
                    {
                        float pdJi = pdJ[b];
                        pm[b] = beta1 * pm[b] + (1 - beta1) * pdJi;
                        pu[b] = (beta2 * pu[b]).Max(pdJi.Abs());
                        pb[b] -= eta / (1 - b1t) * pm[b] / pu[b];
                    }
                }
            }

            return Minimize;
        }
    }
}
