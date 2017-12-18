﻿using Alea;
using Alea.cuDNN;
using JetBrains.Annotations;
using NeuralNetworkNET.Cuda.Services;
using NeuralNetworkNET.Cuda.Extensions;
using NeuralNetworkNET.Networks.Implementations.Layers;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.APIs.Enums;

namespace NeuralNetworkNET.Cuda.Layers
{
    /// <summary>
    /// A softmax output layer based on the cuDNN back-end
    /// </summary>
    internal sealed class CuDnnSoftmaxLayer : SoftmaxLayer
    {
        #region cuDNN fields

        // The NCHW tensor info for the layer softmax activation outputs
        [NotNull]
        private readonly TensorDescriptor SoftmaxInfo = new TensorDescriptor();

        /// <summary>
        /// Gets the <see cref="Dnn"/> instance for the current layer
        /// </summary>
        [NotNull]
        private readonly Dnn DnnInstance = DnnService.Instance;

        #endregion

        public CuDnnSoftmaxLayer(in TensorInfo input, int outputs, BiasInitializationMode biasMode) : base(input, outputs, biasMode) { }

        public CuDnnSoftmaxLayer([NotNull] float[,] weights, [NotNull] float[] biases) : base(weights, biases) { }

        /// <inheritdoc/>
        public override unsafe void Forward(in Tensor x, out Tensor z, out Tensor a)
        {
            using (DeviceMemory<float> z_gpu = DnnInstance.Gpu.AllocateDevice<float>(x.Entities * OutputInfo.Size))
            {
                // Linear pass
                fixed (float* pw = Weights)
                {
                    Tensor.Fix(pw, InputInfo.Size, OutputInfo.Size, out Tensor wTensor);
                    using (DeviceMemory<float>
                        x_gpu = DnnInstance.Gpu.AllocateDevice(x),
                        w_gpu = DnnInstance.Gpu.AllocateDevice(wTensor),
                        b_gpu = DnnInstance.Gpu.AllocateDevice(Biases))
                    {
                        DnnInstance.FullyConnectedForward(x.Entities, x.Length, OutputInfo.Size, x_gpu.Ptr, w_gpu.Ptr, b_gpu.Ptr, z_gpu.Ptr);
                        z_gpu.CopyToHost(x.Entities, OutputInfo.Size, out z);
                    }
                }

                // Activation
                SoftmaxInfo.Set4D(DataType.FLOAT, TensorFormat.CUDNN_TENSOR_NCHW, x.Entities, OutputInfo.Size, 1, 1);
                using (DeviceMemory<float> y_gpu = DnnInstance.Gpu.AllocateDevice<float>(z.Size))
                {
                    DnnInstance.SoftmaxForward(SoftmaxAlgorithm.FAST, SoftmaxMode.INSTANCE, 1, SoftmaxInfo, z_gpu.Ptr, 0, SoftmaxInfo, y_gpu.Ptr);
                    y_gpu.CopyToHost(x.Entities, OutputInfo.Size, out a);
                }
            }
        }
    }
}