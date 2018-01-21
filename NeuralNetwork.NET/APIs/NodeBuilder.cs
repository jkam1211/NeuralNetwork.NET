﻿using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using NeuralNetworkNET.APIs.Delegates;
using NeuralNetworkNET.APIs.Enums;

namespace NeuralNetworkNET.APIs
{
    /// <summary>
    /// A class that represents a declaration for a graph node to build
    /// </summary>
    public sealed class NodeBuilder
    {
        #region Fields and initializaation

        // The factory for a node with an underlying network layer
        [CanBeNull]
        internal LayerFactory Factory { get; }

        // The instance node type
        internal ComputationGraphNodeType NodeType { get; }

        /// <summary>
        /// Gets the current list of child <see cref="NodeBuilder"/> instances
        /// </summary>
        [NotNull, ItemNotNull]
        private List<NodeBuilder> Children { get; } = new List<NodeBuilder>();

        private NodeBuilder(ComputationGraphNodeType type, [CanBeNull] LayerFactory factory)
        {
            if (type == ComputationGraphNodeType.Processing && factory == null)
                throw new InvalidOperationException("Invalid node initialization");
            NodeType = type;
            Factory = factory;
        }

        // Static constructor for a node with multiple parents
        private static NodeBuilder New(ComputationGraphNodeType type, [CanBeNull] LayerFactory factory, params NodeBuilder[] inputs)
        {
            if (inputs.Length < 2) throw new ArgumentException("The inputs must be at least two", nameof(inputs));
            NodeBuilder @this = new NodeBuilder(type, factory);
            foreach (NodeBuilder input in inputs)
                input.Children.Add(@this);
            return @this;
        }

        // Constructor for a node with a single parent
        private NodeBuilder New(ComputationGraphNodeType type, [CanBeNull] LayerFactory factory)
        {
            NodeBuilder builder = new NodeBuilder(type, factory);
            Children.Add(builder);
            return builder;
        }

        #endregion

        /// <summary>
        /// Creates a new linear sum node that merges multiple input nodes
        /// </summary>
        /// <param name="inputs">The sequence of parent nodes for the new instance</param>
        [PublicAPI]
        [Pure, NotNull]
        public static NodeBuilder Sum(params NodeBuilder[] inputs) => New(ComputationGraphNodeType.Sum, null, inputs);

        /// <summary>
        /// Creates a new depth concatenation node that merges multiple input nodes with the same output shape
        /// </summary>
        /// <param name="inputs">The sequence of parent nodes for the new instance</param>
        [PublicAPI]
        [Pure, NotNull]
        public static NodeBuilder DepthConcatenation(params NodeBuilder[] inputs) => New(ComputationGraphNodeType.DepthConcatenation, null, inputs);

        /// <summary>
        /// Creates a new node that will host a network layer to process its inputs
        /// </summary>
        /// <param name="factory">The <see cref="LayerFactory"/> instance used to create the network layer</param>
        [PublicAPI]
        [Pure, NotNull]
        public NodeBuilder Layer(LayerFactory factory) => New(ComputationGraphNodeType.Processing, factory);

        /// <summary>
        /// Creates a new node that will route its inputs to a training sug-graph
        /// </summary>
        [PublicAPI]
        [Pure, NotNull]
        public NodeBuilder TrainingBranch() => New(ComputationGraphNodeType.TrainingBranch, null);
    }
}
