﻿using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MnistDatasetToolkit;
using NeuralNetworkNET.APIs;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Results;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.Helpers;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.SupervisedLearning.Optimization.Progress;
using SixLabors.ImageSharp.PixelFormats;

namespace DigitsCudaTest
{
    class Program
    {
        static async Task Main()
        {
            // Parse the dataset and create the network
            (var training, var test) = DataParser.LoadDatasets();
            INeuralNetwork network = NetworkManager.NewSequential(TensorInfo.CreateImage<Alpha8>(28, 28),
                CuDnnNetworkLayers.Convolutional(ConvolutionInfo.Default, (5, 5), 20, ActivationFunctionType.LeakyReLU),
                CuDnnNetworkLayers.Convolutional(ConvolutionInfo.Default, (5, 5), 20, ActivationFunctionType.Identity),
                CuDnnNetworkLayers.Pooling(PoolingInfo.Default, ActivationFunctionType.LeakyReLU),
                CuDnnNetworkLayers.Convolutional(ConvolutionInfo.Default, (3, 3), 40, ActivationFunctionType.LeakyReLU),
                CuDnnNetworkLayers.Convolutional(ConvolutionInfo.Default, (3, 3), 40, ActivationFunctionType.Identity),
                CuDnnNetworkLayers.Pooling(PoolingInfo.Default, ActivationFunctionType.LeakyReLU),
                CuDnnNetworkLayers.FullyConnected(125, ActivationFunctionType.LeCunTanh),
                CuDnnNetworkLayers.FullyConnected(64, ActivationFunctionType.LeCunTanh),
                CuDnnNetworkLayers.Softmax(10));

            // Setup and start the training
            CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => cts.Cancel();
            TrainingSessionResult result = await NetworkManager.TrainNetworkAsync(network, 
                DatasetLoader.Training(training, 400), 
                TrainingAlgorithms.Adadelta(),
                20, 0.5f,
                new Progress<BatchProgress>(p =>
                {
                    Console.SetCursorPosition(0, Console.CursorTop);
                    int n = (int)(p.Percentage * 32 / 100);
                    char[] c = new char[32];
                    for (int i = 0; i < 32; i++) c[i] = i <= n ? '=' : ' ';
                    Console.Write($"[{new String(c)}] ");
                }),
                testDataset: DatasetLoader.Test(test, new Progress<TrainingProgressEventArgs>(p =>
                {
                    Printf($"Epoch {p.Iteration}, cost: {p.Result.Cost}, accuracy: {p.Result.Accuracy}");
                })), token: cts.Token);

            // Save the training reports
            String
                timestamp = DateTime.Now.ToString("yy-MM-dd-hh-mm-ss"),
                root = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location)),
                path = Path.Combine(root ?? throw new InvalidOperationException("The dll path can't be null"), "TrainingResults", timestamp);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, $"{timestamp}_cost.py"), result.TestReports.AsPythonMatplotlibChart(TrainingReportType.Cost));
            File.WriteAllText(Path.Combine(path, $"{timestamp}_accuracy.py"), result.TestReports.AsPythonMatplotlibChart(TrainingReportType.Accuracy));
            network.Save(new FileInfo(Path.Combine(path, $"{timestamp}{NetworkLoader.NetworkFileExtension}")));
            File.WriteAllText(Path.Combine(path, $"{timestamp}.json"), network.SerializeMetadataAsJson());
            File.WriteAllText(Path.Combine(path, $"{timestamp}_report.json"), result.SerializeAsJson());
            Printf($"Stop reason: {result.StopReason}, elapsed time: {result.TrainingTime}");
            Console.ReadKey();
        }

        // Prints an output message
        private static void Printf(String text)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Write(">> ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{text}\n");
        }
    }
}
