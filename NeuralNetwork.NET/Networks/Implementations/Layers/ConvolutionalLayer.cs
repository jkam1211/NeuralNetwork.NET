﻿using JetBrains.Annotations;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.Networks.Activations.Delegates;
using NeuralNetworkNET.Networks.Implementations.Layers.Abstract;
using NeuralNetworkNET.Networks.Implementations.Layers.Helpers;
using Newtonsoft.Json;
using System.IO;
using System.Runtime.CompilerServices;

namespace NeuralNetworkNET.Networks.Implementations.Layers
{
    /// <summary>
    /// A convolutional layer, used in a CNN network
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal class ConvolutionalLayer : WeightedLayerBase
    {
        #region Parameters

        /// <inheritdoc/>
        public override LayerType LayerType { get; } = LayerType.Convolutional;

        [JsonProperty(nameof(OperationInfo), Order = 6)]
        private readonly ConvolutionInfo _OperationInfo;

        /// <summary>
        /// Gets the info on the convolution operation performed by the layer
        /// </summary>    
        public ref readonly ConvolutionInfo OperationInfo
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _OperationInfo;
        }

        [JsonProperty(nameof(KernelInfo), Order = 7)]
        public readonly TensorInfo _KernelInfo;

        /// <summary>
        /// Gets the <see cref="TensorInfo"/> associated with each kernel in the layer
        /// </summary>

        public ref readonly TensorInfo KernelInfo
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _KernelInfo;
        }

        /// <summary>
        /// Gets the number of kernels in the current layer
        /// </summary>
        [JsonProperty(nameof(Kernels), Order = 8)]
        public int Kernels => OutputInfo.Channels;

        #endregion

        public ConvolutionalLayer(in TensorInfo input, in ConvolutionInfo operation, (int X, int Y) kernelSize, int kernels, ActivationFunctionType activation, BiasInitializationMode biasMode)
            : base(input, new TensorInfo(input.Height - kernelSize.X + 1, input.Width - kernelSize.Y + 1, kernels),
                  WeightsProvider.NewConvolutionalKernels(input.Channels, kernelSize.X, kernelSize.Y, kernels),
                  WeightsProvider.NewBiases(kernels, biasMode), activation)
        {
            _OperationInfo = operation;
            _KernelInfo = new TensorInfo(kernelSize.X, kernelSize.Y, input.Channels);
        }

        public ConvolutionalLayer(
            in TensorInfo input, in ConvolutionInfo operation, in TensorInfo kernels, in TensorInfo output,
            [NotNull] float[] weights, [NotNull] float[] biases, ActivationFunctionType activation)
            : base(input, output, weights, biases, activation)
        {
            _OperationInfo = operation;
            _KernelInfo = kernels;
        }

        /// <inheritdoc/>
        public override unsafe void Forward(in Tensor x, out Tensor z, out Tensor a)
        {
            fixed (float* pw = Weights)
            {
                Tensor.Reshape(pw, OutputInfo.Channels, KernelInfo.Size, out Tensor wTensor);
                x.ConvoluteForward(InputInfo, wTensor, KernelInfo, Biases, out z);
                if (ActivationFunctionType == ActivationFunctionType.Identity) Tensor.From(z, z.Entities, z.Length, out a);
                else z.Activation(ActivationFunctions.Activation, out a);
            }
        }

        /// <inheritdoc/>
        public override unsafe void Backpropagate(in Tensor delta_1, in Tensor z, ActivationFunction activationPrime)
        {
            fixed (float* pw = Weights)
            {
                Tensor.Reshape(pw, OutputInfo.Channels, KernelInfo.Size, out Tensor wTensor);
                wTensor.Rotate180(KernelInfo.Channels, out Tensor w180);
                delta_1.ConvoluteBackwards(OutputInfo, w180, KernelInfo, out Tensor delta);
                w180.Free();
                z.InPlaceActivationAndHadamardProduct(delta, activationPrime);
                delta.Free();
            }
        }

        /// <inheritdoc/>
        public override void ComputeGradient(in Tensor a, in Tensor delta, out Tensor dJdw, out Tensor dJdb)
        {
            a.Rotate180(InputInfo.Channels, out Tensor a180);
            a180.ConvoluteGradient(InputInfo, delta, OutputInfo, out dJdw);
            a180.Free();
            delta.CompressVertically(OutputInfo.Channels, out dJdb);
        }

        /// <inheritdoc/>
        public override INetworkLayer Clone() => new ConvolutionalLayer(InputInfo, OperationInfo, KernelInfo, OutputInfo, Weights.BlockCopy(), Biases.BlockCopy(), ActivationFunctionType);

        /// <inheritdoc/>
        public override void Serialize([NotNull] Stream stream)
        {
            base.Serialize(stream);
            stream.Write(OperationInfo);
            stream.Write(KernelInfo);
        }
    }
}
