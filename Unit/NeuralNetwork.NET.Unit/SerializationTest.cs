﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeuralNetworkNET.APIs;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Helpers;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.Networks.Cost;
using NeuralNetworkNET.Networks.Implementations;

namespace NeuralNetworkNET.Unit
{
    /// <summary>
    /// Test class for the serialization methods
    /// </summary>
    [TestClass]
    [TestCategory(nameof(SerializationTest))]
    public class SerializationTest
    {
        [TestMethod]
        public void StreamSerialize()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                float[,] m = ThreadSafeRandom.NextGlorotNormalMatrix(784, 30);
                stream.Write(m);
                byte[] test = new byte[10];
                stream.Seek(-10, SeekOrigin.Current);
                stream.Read(test, 0, 10);
                Assert.IsTrue(test.Any(b => b != 0));
                Assert.IsTrue(stream.Position == sizeof(float) * m.Length);
                stream.Seek(0, SeekOrigin.Begin);
                float[,] copy = stream.ReadFloatArray(784, 30);
                Assert.IsTrue(m.ContentEquals(copy));
            }
            using (MemoryStream stream = new MemoryStream())
            {
                float[] v = ThreadSafeRandom.NextGaussianVector(723);
                stream.Write(v);
                byte[] test = new byte[10];
                stream.Seek(-10, SeekOrigin.Current);
                stream.Read(test, 0, 10);
                Assert.IsTrue(test.Any(b => b != 0));
                Assert.IsTrue(stream.Position == sizeof(float) * v.Length);
                stream.Seek(0, SeekOrigin.Begin);
                float[] copy = stream.ReadFloatArray(723);
                Assert.IsTrue(v.ContentEquals(copy));
            }
            using (MemoryStream stream = new MemoryStream())
            {
                stream.Write(6);
                stream.Write(677);
                stream.Write(int.MaxValue);
                stream.Seek(0, SeekOrigin.Begin);
                Assert.IsTrue(stream.ReadInt32() == 6);
                Assert.IsTrue(stream.ReadInt32() == 677);
                Assert.IsTrue(stream.ReadInt32() == int.MaxValue);
            }
        }

        [TestMethod]
        public void BinarySerialize1()
        {
            INeuralNetwork network = new NeuralNetwork(
                NetworkLayers.FullyConnected(TensorInfo.CreateLinear(784), 30, ActivationFunctionType.Sigmoid),
                NetworkLayers.FullyConnected(TensorInfo.CreateLinear(30), 10, ActivationFunctionType.Sigmoid, CostFunctionType.CrossEntropy));
            String folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            network.Save(new DirectoryInfo(folderPath), "test1");
            INeuralNetwork copy = NeuralNetworkLoader.TryLoad($"{Path.Combine(folderPath, "test1")}.nnet");
            Assert.IsTrue(copy != null);
            Assert.IsTrue(copy.Equals(network));
        }

        [TestMethod]
        public void BinarySerialize2()
        {
            INeuralNetwork network = new NeuralNetwork(
                NetworkLayers.Convolutional(new TensorInfo(28, 28, 1), (5, 5), 20, ActivationFunctionType.Identity),
                NetworkLayers.Pooling(new TensorInfo(24, 24, 20), ActivationFunctionType.ReLU),
                NetworkLayers.Convolutional(new TensorInfo(12, 12, 20), (5, 5), 10, ActivationFunctionType.Identity),
                NetworkLayers.Pooling(new TensorInfo(8, 8, 10), ActivationFunctionType.ReLU),
                NetworkLayers.FullyConnected(TensorInfo.CreateLinear(160), 8, ActivationFunctionType.Sigmoid),
                NetworkLayers.FullyConnected(TensorInfo.CreateLinear(8), 4, ActivationFunctionType.Sigmoid, CostFunctionType.CrossEntropy));
            String folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            network.Save(new DirectoryInfo(folderPath), "test2");
            INeuralNetwork copy = NeuralNetworkLoader.TryLoad($"{Path.Combine(folderPath, "test2")}.nnet");
            Assert.IsTrue(copy != null);
            Assert.IsTrue(copy.Equals(network));
        }
    }
}
