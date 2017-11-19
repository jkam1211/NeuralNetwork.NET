﻿using System;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.Networks.Activations.Delegates;
using NeuralNetworkNET.Networks.Implementations.Layers.Abstract;
using NeuralNetworkNET.Networks.Implementations.Layers.APIs;
using NeuralNetworkNET.Networks.Implementations.Layers.Helpers;
using NeuralNetworkNET.Networks.Implementations.Misc;

namespace NeuralNetworkNET.Networks.Implementations.Layers
{
    /// <summary>
    /// A convolutional layer, used in a CNN network
    /// </summary>
    internal class ConvolutionalLayer : WeightedLayerBase, INetworkLayer3D
    {
        #region Parameters

        /// <inheritdoc/>
        public override int Inputs => InputVolume.Size;

        /// <inheritdoc/>
        public override int Outputs => OutputVolume.Size;

        /// <inheritdoc/>
        public VolumeInformation InputVolume { get; }

        /// <summary>
        /// Gets the <see cref="VolumeInformation"/> associated with each kernel in the layer
        /// </summary>
        public VolumeInformation KernelVolume { get; }

        /// <inheritdoc/>
        public VolumeInformation OutputVolume { get; }

        #endregion

        public ConvolutionalLayer(VolumeInformation input, VolumeInformation kernelInfo, int kernels, ActivationFunctionType activation)
            : base(WeightsProvider.ConvolutionalKernels(kernelInfo, kernels), WeightsProvider.Biases(kernels), activation)
        {
            InputVolume = InputVolume;
            KernelVolume = kernelInfo;
            OutputVolume = new VolumeInformation(input.Axis - kernelInfo.Axis + 1, kernels);
        }

        public override (float[,] Z, float[,] A) Forward(float[,] x)
        {
            throw new NotImplementedException();
        }

        public override float[,] Backpropagate(float[,] delta_1, float[,] z, ActivationFunction activationPrime)
        {
            throw new NotImplementedException();
        }

        public override LayerGradient ComputeGradient(float[,] a, float[,] delta)
        {
            throw new NotImplementedException();
        }
    }
}
