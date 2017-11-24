﻿namespace NeuralNetworkNET.Networks.Implementations.Layers.APIs
{
    /// <summary>
    /// Indicates the type of a neural network layer (for serialization purposes only)
    /// </summary>
    public enum LayerType : byte
    {
        FullyConnected,
        Convolutional,
        Pooling,
        Output,
        Softmax
    }
}