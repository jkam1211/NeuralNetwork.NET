﻿using Alea;
using Alea.cuDNN;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.cpuDNN;
using NeuralNetworkNET.cuDNN;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Helpers;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.Networks.Layers.Cpu;
using NeuralNetworkNET.Networks.Layers.Initialization;

namespace NeuralNetworkNET.Cuda.Unit
{
    /// <summary>
    /// Test class for the cuDNN extensions
    /// </summary>
    [TestClass]
    [TestCategory(nameof(CuDnnTest))]
    public class CuDnnTest
    {
        // Creates a new random tensor with the given shape
        [Pure]
        private static unsafe Tensor CreateRandomTensor(int entities, int length)
        {
            float[] v = WeightsProvider.NewFullyConnectedWeights(TensorInfo.Linear(entities), length, WeightsInitializationMode.GlorotNormal);
            Tensor.New(entities, length, out Tensor tensor);
            fixed (float* pv = v)
            {
                Tensor.Reshape(pv, entities, length, out Tensor source);
                tensor.Overwrite(source);
                return tensor;
            }
        }

        #region Activation

        [TestMethod]
        public void ActivationForward()
        {
            Tensor x = CreateRandomTensor(400, 1200);
            Tensor.Like(x, out Tensor y1);
            CpuDnn.ActivationForward(x, ActivationFunctions.Sigmoid, y1);
            Gpu gpu = Gpu.Default;
            using (DeviceMemory<float>
                x_gpu = gpu.AllocateDevice(x),
                y_gpu = gpu.AllocateDevice<float>(x.Size))
            {
                Dnn.Get(gpu).ActivationForward(x.Entities, x.Length, x_gpu.Ptr, y_gpu.Ptr, ActivationFunctions.Sigmoid);
                y_gpu.CopyToHost(y1.Entities, y1.Length, out Tensor y2);
                Assert.IsTrue(y1.ContentEquals(y2));
                Tensor.Free(x, y1, y2);
            }
        }

        [TestMethod]
        public void ActivationBackward()
        {
            Tensor
                x = CreateRandomTensor(400, 1200),
                dy = CreateRandomTensor(400, 1200);
            Tensor.Like(x, out Tensor dx1);
            CpuDnn.ActivationBackward(x, dy, ActivationFunctions.SigmoidPrime, dx1);
            Gpu gpu = Gpu.Default;
            using (DeviceMemory<float>
                x_gpu = gpu.AllocateDevice(x),
                dy_gpu = gpu.AllocateDevice(dy))
            {
                Dnn.Get(gpu).ActivationBackward(x.Entities, x.Length, x_gpu.Ptr, dy_gpu.Ptr, ActivationFunctions.SigmoidPrime, dy_gpu.Ptr);
                dy_gpu.CopyToHost(dy.Entities, dy.Length, out Tensor dx2);
                Assert.IsTrue(dx1.ContentEquals(dx2));
                Tensor.Free(x, dy, dx1, dx2);
            }
        }

        #endregion

        #region Fully connected

        [TestMethod]
        public unsafe void FullyConnectedForward()
        {
            FullyConnectedLayer fc = new FullyConnectedLayer(TensorInfo.Linear(231), 125, ActivationType.Sigmoid, WeightsInitializationMode.GlorotUniform, BiasInitializationMode.Gaussian);
            Tensor x = CreateRandomTensor(400, fc.InputInfo.Size);
            fixed (float* pw = fc.Weights, pb = fc.Biases)
            {
                Tensor.Reshape(pw, fc.InputInfo.Size, fc.OutputInfo.Size, out Tensor w);
                Tensor.Reshape(pb, 1, fc.OutputInfo.Size, out Tensor b);
                Tensor.New(x.Entities, fc.OutputInfo.Size, out Tensor y1);
                CpuDnn.FullyConnectedForward(x, w, b, y1);
                Gpu gpu = Gpu.Default;
                using (DeviceMemory<float>
                    x_gpu = gpu.AllocateDevice(x),
                    w_gpu = gpu.AllocateDevice(w),
                    b_gpu = gpu.AllocateDevice(b),
                    y_gpu = gpu.AllocateDevice<float>(y1.Size))
                {
                    Dnn.Get(gpu).FullyConnectedForward(x.Entities, x.Length, y1.Length, x_gpu.Ptr, w_gpu.Ptr, b_gpu.Ptr, y_gpu.Ptr);
                    y_gpu.CopyToHost(y1.Entities, y1.Length, out Tensor y2);
                    Assert.IsTrue(y1.ContentEquals(y2));
                    Tensor.Free(x, y1, y2);
                }
            }
        }

        [TestMethod]
        public unsafe void FullyConnectedBackwardData()
        {
            FullyConnectedLayer fc = new FullyConnectedLayer(TensorInfo.Linear(231), 125, ActivationType.Sigmoid, WeightsInitializationMode.GlorotUniform, BiasInitializationMode.Gaussian);
            Tensor dy = CreateRandomTensor(400, fc.OutputInfo.Size);
            fixed (float* pw = fc.Weights, pb = fc.Biases)
            {
                Tensor.Reshape(pw, fc.InputInfo.Size, fc.OutputInfo.Size, out Tensor w);
                Tensor.Reshape(pb, 1, fc.OutputInfo.Size, out Tensor b);
                Tensor.New(dy.Entities, fc.InputInfo.Size, out Tensor dx1);
                CpuDnn.FullyConnectedBackwardData(w, dy, dx1);
                Gpu gpu = Gpu.Default;
                using (DeviceMemory<float>
                    dy_gpu = gpu.AllocateDevice(dy),
                    w_gpu = gpu.AllocateDevice(w),
                    dx_gpu = gpu.AllocateDevice<float>(dx1.Size))
                {
                    Dnn.Get(gpu).FullyConnectedBackwardData(dy.Entities, fc.InputInfo.Size, fc.OutputInfo.Size, dy_gpu.Ptr, w_gpu.Ptr, dx_gpu.Ptr);
                    dx_gpu.CopyToHost(dx1.Entities, dx1.Length, out Tensor dx2);
                    Assert.IsTrue(dx1.ContentEquals(dx2));
                    Tensor.Free(dy, dx1, dx2);
                }
            }
        }

        [TestMethod]
        public void FullyConnectedBackwardFilter()
        {
            FullyConnectedLayer fc = new FullyConnectedLayer(TensorInfo.Linear(231), 125, ActivationType.Sigmoid, WeightsInitializationMode.GlorotUniform, BiasInitializationMode.Gaussian);
            Tensor
                x = CreateRandomTensor(400, fc.InputInfo.Size),
                dy = CreateRandomTensor(x.Entities, fc.OutputInfo.Size);
            Tensor.New(fc.InputInfo.Size, fc.OutputInfo.Size, out Tensor dJdw1);
            CpuDnn.FullyConnectedBackwardFilter(x, dy, dJdw1);
            dJdw1.Reshape(1, dJdw1.Size, out dJdw1);
            Gpu gpu = Gpu.Default;
            using (DeviceMemory<float>
                x_gpu = gpu.AllocateDevice(x),
                dy_gpu = gpu.AllocateDevice(dy),
                djdb_gpu = gpu.AllocateDevice<float>(fc.Weights.Length))
            {
                Dnn.Get(gpu).FullyConnectedBackwardFilter(x.Entities, fc.InputInfo.Size, fc.OutputInfo.Size, x_gpu.Ptr, dy_gpu.Ptr, djdb_gpu.Ptr);
                djdb_gpu.CopyToHost(1, fc.Weights.Length, out Tensor dJdw2);
                Assert.IsTrue(dJdw1.ContentEquals(dJdw2));
                Tensor.Free(x, dy, dJdw1, dJdw2);
            }
        }

        #endregion

        #region Batch normalization

        [TestMethod]
        public void PerActivationBatchNormalizationForward()
        {
            // Setup
            Tensor x = CreateRandomTensor(400, 250);
            Tensor.NewZeroed(1, 250, out Tensor mu);
            Tensor.LikeZeroed(mu, out Tensor sigma2);
            Tensor.New(1, 250, out Tensor gamma);
            Tensor.NewZeroed(1, 250, out Tensor beta);
            for (int i = 0; i < 250; i++) gamma[i] = ThreadSafeRandom.NextFloat();

            // Cpu
            Tensor.Like(x, out Tensor y1);
            CpuDnn.BatchNormalizationForward(NormalizationMode.PerActivation, TensorInfo.Linear(250), x, mu, sigma2, gamma, beta, y1);

            // Gpu
            Gpu gpu = Gpu.Default;
            using (DeviceMemory<float>
                x_gpu = gpu.AllocateDevice(x),
                y_gpu = gpu.AllocateDevice<float>(x.Size),
                gamma_gpu = gpu.AllocateDevice(gamma),
                beta_gpu = gpu.AllocateDevice(beta),
                run_mean = gpu.AllocateDevice<float>(mu.Size),
                run_var = gpu.AllocateDevice<float>(mu.Size))
            {
                TensorDescriptor desc = new TensorDescriptor();
                desc.Set4D(DataType.FLOAT, TensorFormat.CUDNN_TENSOR_NCHW, x.Entities, x.Length, 1, 1);
                TensorDescriptor gammaBetadesc = new TensorDescriptor();
                gammaBetadesc.Set4D(DataType.FLOAT, TensorFormat.CUDNN_TENSOR_NCHW, 1, x.Length, 1, 1);
                Dnn.Get(gpu).BatchNormalizationForwardTraining(
                    BatchNormMode.PER_ACTIVATION, 1, 0, 
                    desc, x_gpu.Ptr, desc, y_gpu.Ptr, 
                    gammaBetadesc, gamma_gpu.Ptr, beta_gpu.Ptr, 
                    1, run_mean.Ptr, run_var.Ptr, CpuDnn.CUDNN_BN_MIN_EPSILON, 
                    default, default);
                y_gpu.CopyToHost(x.Entities, x.Length, out Tensor y2);
                run_mean.CopyToHost(1, 250, out Tensor runmean);
                run_var.CopyToHost(1, 250, out Tensor runvar);
                Assert.IsTrue(y1.ContentEquals(y2, 1e-5f));

                // Tests
                Assert.IsTrue(mu.ContentEquals(runmean, 1e-5f));
                Assert.IsTrue(sigma2.ContentEquals(runvar, 1e-5f));
            }
        }

        [TestMethod]
        public unsafe void BatchNormalizationBackwardData()
        {
            // Forward
            Tensor x = CreateRandomTensor(400, 250);
            Tensor.New(1, 250, out Tensor mu);
            Tensor.Like(mu, out Tensor sigma2);
            Tensor.Like(x, out Tensor y);
            float[]
                gamma = new float[250],
                beta = new float[250];
            fixed (float* pw = gamma, pb = beta)
            {
                Tensor.Reshape(pw, 1, 250, out Tensor w);
                Tensor.Reshape(pb, 1, 250, out Tensor b);
                CpuDnn.BatchNormalizationForward(NormalizationMode.PerActivation, TensorInfo.Linear(250), x, mu, sigma2, w, b, y);

                // Backward
                Tensor dy = CreateRandomTensor(400, 250);
                Tensor.Like(x, out Tensor dx1);
                CpuDnn.BatchNormalizationBackwardData(NormalizationMode.PerActivation, TensorInfo.Linear(250), x, mu, sigma2, w, dy, dx1);
                Gpu gpu = Gpu.Default;
                using (DeviceMemory<float>
                    x_gpu = gpu.AllocateDevice(x),
                    mu_gpu = gpu.AllocateDevice(mu),
                    sigma2_gpu = gpu.AllocateDevice(sigma2),
                    gamma_gpu = gpu.AllocateDevice(w),
                    dy_gpu = gpu.AllocateDevice(dy),
                    dx_gpu = gpu.AllocateDevice<float>(x.Size))
                {
                    Dnn.Get(gpu).BatchNormalizationBackwardData(x.Entities, x.Length, x_gpu.Ptr, mu_gpu.Ptr, sigma2_gpu.Ptr, gamma_gpu.Ptr, dy_gpu.Ptr, dx_gpu.Ptr);
                    dx_gpu.CopyToHost(x.Entities, x.Length, out Tensor dx2);
                    Assert.IsTrue(dx1.ContentEquals(dx2));
                }
            }
        }

        #endregion
    }
}
