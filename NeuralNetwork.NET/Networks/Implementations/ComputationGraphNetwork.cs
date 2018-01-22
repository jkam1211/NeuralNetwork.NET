﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.cpuDNN;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Helpers;
using NeuralNetworkNET.Networks.Graph;
using NeuralNetworkNET.Networks.Layers.Abstract;
using NeuralNetworkNET.SupervisedLearning.Data;
using NeuralNetworkNET.SupervisedLearning.Optimization;

namespace NeuralNetworkNET.Networks.Implementations
{
    /// <summary>
    /// A computational graph network with an arbitrary internal structure and number of computation branches
    /// </summary>
    internal sealed class ComputationGraphNetwork : NeuralNetworkBase
    {
        #region Parameters

        /// <inheritdoc/>
        public override ref readonly TensorInfo InputInfo => ref Graph.Root.Children[0].To<IComputationGraphNode, ProcessingNode>().Layer.InputInfo;
        
        /// <inheritdoc/>
        public override ref readonly TensorInfo OutputInfo => ref OutputLayer.OutputInfo;

        /// <inheritdoc/>
        public override IReadOnlyList<INetworkLayer> Layers { get; }

        /// <inheritdoc/>
        protected override OutputLayerBase OutputLayer { get; }

        /// <summary>
        /// The underlying layers graph for the network
        /// </summary>
        [NotNull]
        private readonly ComputationGraph Graph;
        
        #endregion

        public ComputationGraphNetwork([NotNull] ComputationGraph graph) : base(NetworkType.ComputationGraph)
        {
            Graph = graph;
            OutputLayer = Graph.OutputNode.To<IComputationGraphNode, ProcessingNode>().Layer.To<INetworkLayer, OutputLayerBase>();
            Layers = Graph.ProcessingNodes.Select(node => node.Layer).ToArray();
            WeightedLayersIndexes = Layers.Select((l, i) => (Layer: l as WeightedLayerBase, Index: i)).Where(t => t.Layer != null).Select(t => t.Index).ToArray();
        }

        #region Implementation

        /// <inheritdoc/>
        protected override unsafe void Forward(in Tensor x, out Tensor yHat)
        {
            // Local mapping
            using (TensorMap<IComputationGraphNode> aMap = new TensorMap<IComputationGraphNode>())
            {
                // Recursive forward function
                Tensor xc = x; // Local copy for closure
                void Forward(IComputationGraphNode node)
                {
                    switch (node)
                    {
                        case ProcessingNode processing:
                            processing.Layer.To<INetworkLayer, NetworkLayerBase>().Forward(processing.Parent is InputNode ? xc : aMap[processing.Parent], out Tensor z, out Tensor a);
                            z.Free();
                            aMap[processing] = a;
                            if (processing == Graph.OutputNode) return;
                            for (int i = 0; i < processing.Children.Count; i++)
                                Forward(processing.Children[i]);
                            break;
                        case MergeNode merge:

                            // Prepare the inputs
                            Tensor* xs = stackalloc Tensor[merge.Parents.Count];
                            for (int i = 0; i < merge.Parents.Count; i++)
                                if (!aMap.TryGetValue(merge.Parents[i], out xs[i])) break;
                            Span<Tensor> inputs = new Span<Tensor>(xs, merge.Parents.Count);

                            // Forward through the merge node
                            Tensor.New(xs[0].Entities, xs[0].Length, out Tensor m);
                            if (merge.Type == ComputationGraphNodeType.Sum) CpuBlas.Sum(inputs, m);
                            else if (merge.Type == ComputationGraphNodeType.DepthConcatenation) CpuDnn.DepthConcatenationForward(inputs, m);
                            else throw new ArgumentOutOfRangeException(nameof(merge.Type), "Unsupported node type");
                            aMap[merge] = m;
                            for (int i = 0; i < merge.Children.Count; i++)
                                Forward(merge.Children[i]);
                            break;
                        case TrainingNode _: return;
                        default:
                            throw new ArgumentException("The node type is not supported", nameof(node));
                    }
                }

                // Manually start the forward pass on the first input branches
                foreach (IComputationGraphNode child in Graph.Root.Children) Forward(child);

                // Collect the outputs and return
                yHat = aMap[Graph.OutputNode];
                aMap.Remove(Graph.OutputNode); // Remove yHat from the map to keep it allocated
            }
        }

        /// <inheritdoc/>
        [SuppressMessage("ReSharper", "AccessToDisposedClosure")] // Tensor maps in optimization closure
        internal override unsafe void Backpropagate(in SamplesBatch batch, float dropout, WeightsUpdater updater)
        {
            fixed (float* px = batch.X, py = batch.Y)
            {
                Tensor.Reshape(px, batch.X.GetLength(0), batch.Y.GetLength(1), out Tensor x);
                Tensor.Reshape(py, batch.Y.GetLength(0), batch.Y.GetLength(1), out Tensor y);

                // Local mapping
                using (TensorMap<IComputationGraphNode> 
                    zMap = new TensorMap<IComputationGraphNode>(),
                    aMap = new TensorMap<IComputationGraphNode>(),
                    dropMap = new TensorMap<IComputationGraphNode>(),
                    dMap = new TensorMap<IComputationGraphNode>(),
                    dJdwMap = new TensorMap<IComputationGraphNode>(),
                    dJdbMap = new TensorMap<IComputationGraphNode>())
                {
                    #region Forward

                    /* =================
                     * Forward pass
                     * =================
                     * Propagate the input tensor through the network, across all the
                     * available branches, and store the computed activities, activations and
                     * dropout masks along the way */
                    void Forward(IComputationGraphNode node)
                    {
                        switch (node)
                        {
                            case ProcessingNode processing:
                                processing.Layer.To<INetworkLayer, NetworkLayerBase>().Forward(processing.Parent is InputNode ? x : aMap[processing.Parent], out Tensor z, out Tensor a);
                                zMap[processing] = z;
                                aMap[processing] = a;
                                if (processing.Layer.LayerType == LayerType.FullyConnected && dropout > 0)
                                {
                                    ThreadSafeRandom.NextDropoutMask(a.Entities, a.Length, dropout, out Tensor mask);
                                    CpuBlas.MultiplyElementwise(a, mask, a);
                                    dropMap[processing] = mask;
                                }
                                break;
                            case MergeNode merge:

                                // Prepare the inputs
                                Tensor* xs = stackalloc Tensor[merge.Parents.Count];
                                for (int i = 0; i < merge.Parents.Count; i++)
                                    if (!aMap.TryGetValue(merge.Parents[i], out xs[i])) break;
                                Span<Tensor> inputs = new Span<Tensor>(xs, merge.Parents.Count);

                                // Forward through the merge node
                                Tensor.New(xs[0].Entities, xs[0].Length, out Tensor m);
                                if (merge.Type == ComputationGraphNodeType.Sum) CpuBlas.Sum(inputs, m);
                                else if (merge.Type == ComputationGraphNodeType.DepthConcatenation) CpuDnn.DepthConcatenationForward(inputs, m);
                                else throw new ArgumentOutOfRangeException(nameof(merge.Type), "Unsupported node type");
                                aMap[merge] = m;
                                break;
                            case TrainingNode split: 
                                aMap[split.Parent].Duplicate(out Tensor copy);
                                aMap[node] = copy;
                                break;
                            default:
                                throw new ArgumentException("The node type is not supported", nameof(node));
                        }
                        for (int i = 0; i < node.Children.Count; i++)
                            Forward(node.Children[i]);
                    }

                    // Manually start the forward pass on the first input branches
                    foreach (IComputationGraphNode child in Graph.Root.Children) Forward(child);

                    #endregion

                    /* ===========================================
                     * Calculate delta(L), DJDw(L) and DJDb(L)
                     * ===========================================
                     * Perform the sigmoid prime of zL, the activity on the last layer
                     * Calculate the gradient of C with respect to a
                     * Compute d(L), the Hadamard product of the gradient and the sigmoid prime for L.
                     * NOTE: for some cost functions (eg. log-likelyhood) the sigmoid prime and the Hadamard product
                     *       with the first part of the formula are skipped as that factor is simplified during the calculation of the output delta */
                    void Backward(IComputationGraphNode node)
                    {
                        if (node is InputNode) return;

                        // Prepare the output error delta
                        Tensor dy;
                        if (node.Children.Count == 1) dy = dMap[node.Children[0]];
                        else if (node.Children.Count > 1)
                        {
                            Tensor* dyt = stackalloc Tensor[node.Children.Count];
                            for (int i = 0; i < node.Children.Count; i++)
                            {
                                if (!dMap.ContainsKey(node.Children[i])) return; // Stop if not all deltas are available yet
                                if (node.Children[i] is MergeNode merge && merge.Type == ComputationGraphNodeType.DepthConcatenation)
                                {
                                    int offset = 0, length = -1;
                                    for (int j = 0; j < merge.Parents.Count; j++)
                                    {
                                        if (merge.Parents[j] == node) break;
                                        offset += j == 0 ? 0 : aMap[merge.Parents[j - 1]].Length;
                                        length = aMap[merge.Parents[j]].Length;
                                    }
                                    Tensor.New(x.Entities, length, out dyt[i]);
                                    CpuDnn.DepthConcatenationBackward(dMap[merge], offset, dyt[i]);
                                }
                                else dyt[i] = dMap[node.Children[i]];
                            }
                            Tensor.Like(*dyt, out dy);
                            CpuBlas.Sum(new Span<Tensor>(dyt, node.Children.Count), dy);
                        }
                        else dy = Tensor.Null; // Null when the current node is an output node

                        // Process the current node
                        switch (node)
                        {
                            case ProcessingNode processing:

                                // Backpropagation with optional gradient
                                if (processing.Layer is ConstantLayerBase constant && !(processing.Parent is InputNode))
                                {
                                    Tensor.New(x.Entities, constant.InputInfo.Size, out Tensor dx);
                                    constant.Backpropagate(aMap[processing.Parent], zMap[node], dy, dx);
                                    dMap[node] = dx;
                                }
                                else if (processing.Layer is WeightedLayerBase weighted)
                                {
                                    if (dropMap.TryGetValue(node, out Tensor mask)) CpuBlas.MultiplyElementwise(dy, mask, dy); // Optional dropout
                                    Tensor dJdw, dJdb;
                                    if (processing.Parent is InputNode) weighted.Backpropagate(x, zMap[node], dy, Tensor.Null, out dJdw, out dJdb);
                                    else if (weighted is OutputLayerBase output)
                                    {
                                        Tensor.New(x.Entities, output.InputInfo.Size, out Tensor dx);
                                        output.Backpropagate(aMap[processing.Parent], aMap[node], y, zMap[node], dx, out dJdw, out dJdb);
                                        dMap[node] = dx;
                                    }
                                    else
                                    {
                                        // Gradients and backpropagated error delta
                                        Tensor.New(x.Entities, weighted.InputInfo.Size, out Tensor dx);
                                        weighted.Backpropagate(aMap[processing.Parent], zMap[node], dy, dx, out dJdw, out dJdb);
                                        dMap[node] = dx;
                                    }
                                    dJdwMap[node] = dJdw;
                                    dJdbMap[node] = dJdb;
                                }
                                else throw new InvalidOperationException("Invalid backpropagation node");
                                Backward(processing.Parent);
                                break;
                            case MergeNode merge:
                                dMap[node] = dy; // Pass-through the error delta with no changes
                                for (int i = 0; i < merge.Parents.Count; i++)
                                    Backward(merge.Parents[i]);
                                break;
                            case TrainingNode split:
                                dMap[node] = dy;
                                Backward(split.Parent);
                                break;
                            case InputNode _: return;
                            default:
                                throw new ArgumentException("The node type is not supported", nameof(node));
                        }
                    }

                    // Backpropagate from the training and inference outputs
                    foreach (ProcessingNode output in Graph.TrainingOutputNodes) Backward(output);
                    Backward(Graph.OutputNode);

                    /* ================
                     * Optimization
                     * ================
                     * Edit the network weights according to the computed gradients */
                    int samples = batch.X.GetLength(0);
                    Parallel.For(0, WeightedLayersIndexes.Length, i =>
                    {
                        ProcessingNode node = Graph.ProcessingNodes[WeightedLayersIndexes[i]];
                        updater(i, dJdwMap[node], dJdbMap[node], samples, node.Layer.To<INetworkLayer, WeightedLayerBase>());
                    }).AssertCompleted();
                }
            }
        }

        #endregion

        #region Features extraction

        /// <inheritdoc/>
        public override IReadOnlyList<(float[] Z, float[] A)> ExtractDeepFeatures(float[] x)
        {
            return (from pair in ExtractDeepFeatures(x.AsSpan().AsMatrix(1, x.Length))
                    let z = pair.Z?.AsSpan().ToArray()
                    let a = pair.A.AsSpan().ToArray()
                    select (z, a)).ToArray();
        }

        /// <inheritdoc/>
        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        public override unsafe IReadOnlyList<(float[,] Z, float[,] A)> ExtractDeepFeatures(float[,] x)
        {
            // Local mapping           
            fixed (float* px = x)
            {
                Tensor.Reshape(px, x.GetLength(0), x.GetLength(1), out Tensor xc);
                using (TensorMap<IComputationGraphNode> 
                    zMap = new TensorMap<IComputationGraphNode>(),
                    aMap = new TensorMap<IComputationGraphNode>())
                {
                    void Forward(IComputationGraphNode node)
                    {
                        switch (node)
                        {
                            case ProcessingNode processing:
                                processing.Layer.To<INetworkLayer, NetworkLayerBase>().Forward(processing.Parent is InputNode ? xc : aMap[processing.Parent], out Tensor z, out Tensor a);
                                zMap[processing] = z;
                                aMap[processing] = a;
                                for (int i = 0; i < node.Children.Count; i++)
                                    Forward(node.Children[i]);
                                break;
                            case MergeNode merge:

                                // Prepare the inputs
                                Tensor* xs = stackalloc Tensor[merge.Parents.Count];
                                for (int i = 0; i < merge.Parents.Count; i++)
                                    if (!aMap.TryGetValue(merge.Parents[i], out xs[i])) break;
                                Span<Tensor> inputs = new Span<Tensor>(xs, merge.Parents.Count);

                                // Forward through the merge node
                                Tensor.New(xs[0].Entities, xs[0].Length, out Tensor m);
                                if (merge.Type == ComputationGraphNodeType.Sum) CpuBlas.Sum(inputs, m);
                                else if (merge.Type == ComputationGraphNodeType.DepthConcatenation) CpuDnn.DepthConcatenationForward(inputs, m);
                                else throw new ArgumentOutOfRangeException(nameof(merge.Type), "Unsupported node type");
                                aMap[merge] = m;
                                for (int i = 0; i < node.Children.Count; i++)
                                    Forward(node.Children[i]);
                                break;
                            case TrainingNode _: return;
                            default:
                                throw new ArgumentException("The node type is not supported", nameof(node));
                        }                        
                    }

                    // Manually start the forward pass on the first input branches
                    foreach (IComputationGraphNode child in Graph.Root.Children) Forward(child);

                    // Return the extracted features
                    return Graph.Nodes.Where(node => zMap.ContainsKey(node) || aMap.ContainsKey(node)).Select(node =>
                    {
                        aMap.TryGetValue(node, out Tensor at);
                        float[,] 
                            z = zMap.TryGetValue(node, out Tensor zt) ? zt.ToArray2D() : null,
                            a = at.ToArray2D();
                        return (z, a);
                    }).ToArray();
                }
            }
        }

        #endregion

        /// <inheritdoc/>
        public override INeuralNetwork Clone()
        {
            throw new NotImplementedException();
        }
    }
}
