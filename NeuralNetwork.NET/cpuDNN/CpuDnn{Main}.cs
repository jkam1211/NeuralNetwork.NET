﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.Networks.Activations.Delegates;

namespace NeuralNetworkNET.cpuDNN
{
    /// <summary>
    /// A static class that contains primitives to implement a CNN running on CPU
    /// </summary>
    public static partial class CpuDnn
    {
        #region Activation

        /// <summary>
        /// Executes the input activation function on the target <see cref="Tensor"/>
        /// </summary>
        /// <param name="x">The layer input <see cref="Tensor"/></param>
        /// <param name="f">The activation function to apply to the input</param>
        /// <param name="y">The output <see cref="Tensor"/> - it can be the same as the input</param>
        public static unsafe void ActivationForward(in Tensor x, [NotNull] ActivationFunction f, in Tensor y)
        {
            // Setup
            int n = x.Entities, l = x.Length;
            if (!y.MatchShape(x)) throw new ArgumentException("The target tensor must have the same shape as the input");
            float* py = y, px = x;

            // Execute the activation in parallel
            void Kernel(int i)
            {
                int offset = i * l;
                for (int j = 0; j < l; j++)
                {
                    int target = offset + j;
                    py[target] = f(px[target]);
                }
            }
            Parallel.For(0, n, Kernel).AssertCompleted();
        }

        /// <summary>
        /// Performs the softmax activation on the input <see cref="Tensor"/> and applies the output normalization
        /// </summary>
        /// <param name="x">The input <see cref="Tensor"/></param>
        /// <param name="y">The output <see cref="Tensor"/></param>
        public static unsafe void SoftmaxForward(in Tensor x, in Tensor y)
        {
            // Setup
            if (!x.MatchShape(y)) throw new ArgumentException("The input tensor doesn't have the same shape as the output tensor");
            int n = x.Entities, l = x.Length;
            float* px = x, py = y;

            // Activation
            void ActivationWithAggregate(int i)
            {
                int offset = i * l;
                float sum = 0;
                for (int j = 0; j < l; j++)
                {
                    int target = offset + j;
                    float value = ActivationFunctions.Softmax(px[target]);
                    py[target] = value;
                    sum += value;
                }
                for (int j = 0; j < l; j++)
                    py[offset + j] /= sum;
            }
            Parallel.For(0, n, ActivationWithAggregate).AssertCompleted();
        }

        /// <summary>
        /// Executes the backward activation function on the target <see cref="Tensor"/>, with the given error delta
        /// </summary>
        /// <param name="y">The activity computed in the forwaard pass</param>
        /// <param name="dy">The current error delta to backpropagate</param>
        /// <param name="f_">The derivative of the activation function used in the forward pass</param>
        /// <param name="dx">The resulting input error delta - it can be the same as the input <see cref="Tensor"/></param>
        public static unsafe void ActivationBackward(in Tensor y, in Tensor dy, [NotNull] ActivationFunction f_, in Tensor dx)
        {
            // Check
            if (!dy.MatchShape(y)) throw new ArgumentException("The input tensors must have the same shape", nameof(dy));
            if (!dx.MatchShape(y)) throw new ArgumentException("The output tensor must have the same shape as the input", nameof(dy));
            int
                n = y.Entities,
                l = y.Length;
            float* px = y, pdy = dy, pdx = dx;

            // Loop in parallel
            void Kernel(int i)
            {
                int offset = i * l;
                for (int j = 0; j < l; j++)
                {
                    int target = offset + j;
                    pdx[target] = f_(px[target]) * pdy[target];
                }
            }
            Parallel.For(0, n, Kernel).AssertCompleted();
        }

        #endregion

        #region Fully connected

        /// <summary>
        /// Executes the forward pass on a fully connected layer
        /// </summary>
        /// <param name="x">The input <see cref="Tensor"/> to process</param>
        /// <param name="w">The layer weights</param>
        /// <param name="b">The layer biases</param>
        /// <param name="y">The output <see cref="Tensor"/> for the current layer</param>
        public static unsafe void FullyConnectedForward(in Tensor x, in Tensor w, in Tensor b, in Tensor y)
        {
            // Initialize the parameters and the result tensor
            if (x.Length != w.Entities) throw new ArgumentOutOfRangeException("Invalid tensors shapes");
            if (!b.MatchShape(1, w.Length)) throw new ArgumentException("Invalid biases shape", nameof(b));
            if (!y.MatchShape(x.Entities, w.Length)) throw new ArgumentException("The output tensor doesn't have the right shape", nameof(y));
            int
                h = x.Entities,
                l = x.Length,
                k = w.Length;
            float* pdy = y, px = x, pw = w, pb = b;

            // Execute the multiplication in parallel
            void Kernel(int i)
            {
                int offset = i * l;
                for (int j = 0; j < k; j++)
                {
                    // Perform the multiplication
                    int start = j;
                    float res = 0;
                    for (int q = 0; q < l; q++, start += k)
                    {
                        res += px[offset + q] * pw[start];
                    }
                    pdy[i * k + j] = res + pb[j]; // Sum the input vector to each component
                }
            }
            Parallel.For(0, h, Kernel).AssertCompleted();
        }

        /// <summary>
        /// Executes the backward pass on a fully connected layer
        /// </summary>
        /// <param name="w">The layer weights</param>
        /// <param name="dy">The output error delta</param>
        /// <param name="dx">The resulting input error delta</param>
        public static unsafe void FullyConnectedBackwardData(in Tensor w, in Tensor dy, in Tensor dx)
        {
            if (w.Length != dy.Length) throw new ArgumentException("The weights tensor doesn't have a valid shape", nameof(w));
            if (!dx.MatchShape(dy.Entities, w.Entities)) throw new ArgumentException("The input tensor doesn't have the right shape", nameof(dx));
            Tensor.New(w.Length, w.Entities, out Tensor wt);
            CpuBlas.Transpose(w, wt);

            // Initialize the parameters and the result tensor
            int 
                h = dy.Entities,
                l = dy.Length,
                k = wt.Length;
            float* pdx = dx, pdy = dy, pwt = wt;

            // Execute the multiplication in parallel
            void Kernel(int i)
            {
                int i1 = i * l;
                for (int j = 0; j < k; j++)
                {
                    // Perform the multiplication
                    int i2 = j;
                    float res = 0;
                    for (int q = 0; q < l; q++, i2 += k)
                    {
                        res += pdy[i1 + q] * pwt[i2];
                    }

                    // res has now the tensor multiplication result for position [i, j]
                    pdx[i * k + j] = res;
                }
            }
            Parallel.For(0, h, Kernel).AssertCompleted();
            wt.Free();
        }

        /// <summary>
        /// Executes the backward pass on a fully connected layer to calculate the gradient with respect to the weights
        /// </summary>
        /// <param name="x">The layer inputs</param>
        /// <param name="dy">The layer output error delta</param>
        /// <param name="dw">The resulting weights gradient <see cref="Tensor"/></param>
        public static void FullyConnectedBackwardFilter(in Tensor x, in Tensor dy, in Tensor dw)
        {
            if (x.Entities != dy.Entities) throw new ArgumentException("The input tensor doesn't match the number of samples from the delta", nameof(x));
            Tensor.New(x.Length, x.Entities, out Tensor xt);
            CpuBlas.Transpose(x, xt);
            CpuBlas.Multiply(xt, dy, dw);
            xt.Free();
        }

        /// <summary>
        /// Executes the backward pass on a fully connected layer to calculate the gradient with respect to the biases
        /// </summary>
        /// <param name="dy">The layer output error delta</param>
        /// <param name="db">The resulting biases gradient <see cref="Tensor"/></param>
        public static unsafe void FullyConnectedBackwardBias(in Tensor dy, in Tensor db)
        {
            // Preliminary checks and declarations
            if (!db.MatchShape(1, dy.Length)) throw new ArgumentException("Invalid result tensor shape", nameof(db));
            int
                n = dy.Entities,
                l = dy.Length;
            float* pdy = dy, pdb = db;

            // Compress the tensor
            void Kernel(int j)
            {
                float sum = 0;
                for (int i = 0; i < n; i++)
                    sum += pdy[i * l + j];
                pdb[j] = sum;
            }
            Parallel.For(0, l, Kernel).AssertCompleted();
        }

        #endregion

        #region Batch normalization

        /// <summary>
        /// Executes the forward pass in a batch normalization layer
        /// </summary>
        /// <param name="mode">The desired normalization mode to apply</param>
        /// <param name="info">The ifo on the input <see cref="Tensor"/> to process</param>
        /// <param name="x">The input <see cref="Tensor"/> to normalize</param>
        /// <param name="mu">A <see cref="Tensor"/> to use to store the temporary median values (used for backpropagation too)</param>
        /// <param name="sigma2">A <see cref="Tensor"/> to use to store the temporary standard deviation values (used for backpropagation too)</param>
        /// <param name="gamma">The layer gamma parameters</param>
        /// <param name="beta">The layer beta parameters</param>
        /// <param name="y">The output <see cref="Tensor"/> for the current layer</param>
        public static unsafe void BatchNormalizationForward(
            NormalizationMode mode, in TensorInfo info, in Tensor x, 
            in Tensor mu, in Tensor sigma2, 
            in Tensor gamma, in Tensor beta, in Tensor y)
        {
            // Checks
            if (info.Size != x.Length) throw new ArgumentException("The tensor info doesn't match the length of the input tensor", nameof(x));
            if (!sigma2.MatchShape(mu)) throw new ArgumentException("Invalid standard deviation tensor shape", nameof(sigma2));
            if (!gamma.MatchShape(sigma2)) throw new ArgumentException("The gamma tensor doesn't have the right shape", nameof(gamma));
            if (!beta.MatchShape(gamma)) throw new ArgumentException("The beta tensor doesn't have the right shape", nameof(beta));
            if (!x.MatchShape(y)) throw new ArgumentException("The input and output tensors must have the same shape", nameof(y));

            // Setup
            int
                n = x.Entities,
                l = x.Length;
            float* px = x, pmu = mu, psigma2 = sigma2, py = y, pg = gamma, pb = beta;
            switch (mode)
            {
                #region Spatial

                // A single mu and variance value per input channel
                case NormalizationMode.Spatial:
                    if (!mu.MatchShape(1, info.Channels)) throw new ArgumentException("Invalid mu tensor size");
                    int
                        nhw = x.Entities * info.SliceSize,
                        slice = info.SliceSize;
                    Parallel.For(0, info.Channels, c =>
                    {
                        // Mu
                        float mc = 0;
                        float* start = px + slice * c;
                        for (int i = 0; i < n; i++)
                        {
                            float* offset = start + i * l;
                            for (int xy = 0; xy < slice; xy++)
                                mc += offset[xy];
                        }
                        pmu[c] = mc /= nhw;

                        // Variance
                        float sc = 0;
                        for (int i = 0; i < n; i++)
                        {
                            float* offset = start + i * l;
                            for (int xy = 0; xy < slice; xy++)
                            {
                                mc += offset[xy] - mc;
                                sc += mc * mc;
                            }
                        }
                        psigma2[c] = sc;

                    }).AssertCompleted();

                    // Normalization
                    Parallel.For(0, info.Channels, c =>
                    {
                        float
                            gc = pg[c],
                            bc = pb[c],
                            mc = pmu[c],
                            sqrt_1 = 1 / (float)Math.Sqrt(psigma2[c] + float.Epsilon);
                        float*
                            start = px + slice * c,
                            end = py + slice * c;
                        for (int i = 0; i < n; i++)
                        {
                            float*
                                offset = start + i * l,
                                target = end + i * l;
                            for (int xy = 0; xy < slice; xy++)
                            {
                                float hat = (offset[xy] - mc) * sqrt_1;
                                target[xy] = gc * hat + bc;
                            }
                        }
                    }).AssertCompleted();
                    break;

                #endregion

                #region Per activation

                // Each individual activation has its own median and variance
                case NormalizationMode.PerActivation:
                    if (!mu.MatchShape(1, x.Length)) throw new ArgumentException("Invalid mu tensor size");
                    Parallel.For(0, l, j =>
                    {
                        // Mean
                        float mi = 0;
                        for (int i = 0; i < n; i++)
                            mi += px[i * l + j];
                        pmu[j] = mi /= n;

                        // Variance
                        float sl = 0;
                        for (int i = 0; i < n; i++)
                        {
                            float hm = px[i * l + j] - mi;
                            sl += hm * hm;
                        }
                        psigma2[j] = sl / n;

                    }).AssertCompleted();

                    // Apply the batch normalization pass
                    Parallel.For(0, n, i =>
                    {
                        int offset = i * l;
                        for (int j = 0; j < l; j++)
                        {
                            float hat = (px[offset + j] - pmu[j]) / (float)Math.Sqrt(psigma2[j] + float.Epsilon);
                            py[offset + j] = pg[j] * hat + pb[j];
                        }
                    }).AssertCompleted();
                    break;

                #endregion

                default: throw new ArgumentOutOfRangeException(nameof(mode), "Invalid normalization mode");
            }
        }

        /// <summary>
        /// Executes the backward pass through a batch normalization layer
        /// </summary>
        /// <param name="mode">The desired normalization mode to apply</param>
        /// <param name="info">The ifo on the input <see cref="Tensor"/> to process</param>
        /// <param name="x">The input <see cref="Tensor"/> to normalize</param>
        /// <param name="mu">A <see cref="Tensor"/> with the temporary median values calculated in the forward pass</param>
        /// <param name="sigma2">A <see cref="Tensor"/> with the temporary standard deviation values calculated in the forward pass</param>
        /// <param name="gamma">The layer gamma parameters</param>
        /// <param name="dy">The output error delta <see cref="Tensor"/></param>
        /// <param name="dx">The resulting backpropagated error delta <see cref="Tensor"/></param>
        public static unsafe void BatchNormalizationBackwardData(
            NormalizationMode mode, in TensorInfo info, in Tensor x, 
            in Tensor mu, in Tensor sigma2, in Tensor gamma, 
            in Tensor dy, in Tensor dx)
        {
            // Checks
            if (!sigma2.MatchShape(mu)) throw new ArgumentException("Invalid standard deviation tensor shape", nameof(sigma2));
            if (!gamma.MatchShape(sigma2)) throw new ArgumentException("The gamma tensor doesn't have the right shape", nameof(gamma));
            if (!x.MatchShape(dy)) throw new ArgumentException("The input and output tensors must have the same shape", nameof(dy));
            if (!x.MatchShape(dx)) throw new ArgumentException("The input the resulting error tensor must have the same shape", nameof(dx));

            // Backpropagation
            int
                n = dx.Entities,
                l = dx.Length;
            float* px = x, pg = gamma, pmu = mu, psigma2 = sigma2, pdy = dy, pdx = dx;
            switch (mode)
            {
                case NormalizationMode.Spatial:
                    if (!mu.MatchShape(1, info.Channels)) throw new ArgumentException("Invalid mu tensor size");
                    int
                        nhw = x.Entities * info.SliceSize,
                        slice = info.SliceSize;
                    Parallel.For(0, info.Channels, c =>
                    {
                        // Calculate the two summatories
                        float
                            mc = pmu[c],
                            sc = psigma2[c],
                            left = 1f / nhw * pg[c] / (float)Math.Sqrt(psigma2[c] + float.Epsilon),
                            _2nd = 0,
                            _3rdRight = 0;
                        float*
                            startdy = pdy + slice * c,
                            startx = px + slice * c;
                        for (int i = 0; i < n; i++, startdy += l, startx += l)
                            for (int xy = 0; xy < slice; xy++)
                            {
                                float pdyicxy = startdy[xy];
                                _2nd += pdyicxy;
                                _3rdRight += pdyicxy * (startx[xy] - mc);
                            }

                        // Assign the backpropagated tensor
                        float* startdx = pdx + slice * c;
                        startdy = pdy + slice * c;
                        startx = px + slice * c;
                        for (int i = 0; i < n; i++, startdy += l, startx += l, startdx += l)
                            for (int xy = 0; xy < slice; xy++)
                                startdx[xy] = left * (nhw * startdy[xy] - _2nd - (startx[xy] - mc) / (sc + float.Epsilon) * _3rdRight);

                    }).AssertCompleted();
                    break;
                case NormalizationMode.PerActivation:
                    if (!mu.MatchShape(1, x.Length)) throw new ArgumentException("Invalid mu tensor size");
                    Parallel.For(0, n, i =>
                    {
                        for (int j = 0; j < l; j++)
                        {
                            float
                                left = 1f / n * pg[j] / (float)Math.Sqrt(psigma2[j] + float.Epsilon),
                                _1st = n * pdy[i * l + j],
                                _2nd = 0,
                                _3rdLeft = (px[i * l + j] - pmu[j]) / (psigma2[j] + float.Epsilon),
                                _3rdRight = 0;
                            for (int k = 0; k < n; k++)
                            {
                                float pdykj = pdy[k * l + j];
                                _2nd += pdykj;
                                _3rdRight += pdykj * (px[k * l + j] - pmu[j]);
                            }
                            pdx[i * l + j] = left * (_1st - _2nd - _3rdLeft * _3rdRight);
                        }
                    }).AssertCompleted();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(mode), "Invalid normalization mode");
            }
        }

        /// <summary>
        /// Calculates the gradient with respect to the gamma <see cref="Tensor"/> in a batch normalization layer
        /// </summary>
        /// <param name="mode">The desired normalization mode to apply</param>
        /// <param name="info">The ifo on the input <see cref="Tensor"/> to process</param>
        /// <param name="x">The input <see cref="Tensor"/> used in the forward pass</param>
        /// <param name="mu">A <see cref="Tensor"/> with the temporary median values calculated in the forward pass</param>
        /// <param name="sigma2">A <see cref="Tensor"/> with the temporary standard deviation values calculated in the forward pass</param>
        /// <param name="dy">The output <see cref="Tensor"/> error delta for the current layer</param>
        /// <param name="dgamma">The resulting gamma gradient <see cref="Tensor"/></param>
        public static unsafe void BatchNormalizationBackwardGamma(
            NormalizationMode mode, in TensorInfo info, in Tensor x, 
            in Tensor mu, in Tensor sigma2, 
            in Tensor dy, in Tensor dgamma)
        {
            // Checks
            if (!sigma2.MatchShape(mu)) throw new ArgumentException("Invalid standard deviation tensor shape", nameof(sigma2));
            if (!dgamma.MatchShape(sigma2)) throw new ArgumentException("Invalid gamma gradient tensor size", nameof(dgamma));
            if (!x.MatchShape(dy)) throw new ArgumentException("The input and output tensors must have the same shape", nameof(dy));

            // Gradient with respect to gamma
            int
                n = x.Entities,
                l = x.Length;
            float* px = x, pdy = dy, pdg = dgamma, pmu = mu, psigma2 = sigma2;
            switch (mode)
            {
                case NormalizationMode.Spatial:
                    if (!mu.MatchShape(1, info.Channels)) throw new ArgumentException("Invalid mu tensor size");
                    int slice = info.SliceSize;
                    Parallel.For(0, info.Channels, c =>
                    {
                        float gc = 0, sc = (float)Math.Sqrt(psigma2[c] + float.Epsilon);
                        int offset = slice * c;
                        for (int i = 0; i < n; i++, offset += l)
                            for (int xy = 0; xy < slice; xy++)
                                gc += pdy[offset + xy] * (px[offset + xy] - pmu[c]) / sc;
                        pdg[c] = gc;
                    }).AssertCompleted();
                    break;
                case NormalizationMode.PerActivation:
                    if (!mu.MatchShape(1, x.Length)) throw new ArgumentException("Invalid mu tensor size");
                    Parallel.For(0, x.Length, j =>
                    {
                        float sum = 0, sj = (float)Math.Sqrt(psigma2[j] + float.Epsilon);
                        for (int i = 0; i < n; i++)
                        {
                            float hat = (px[i * l + j] - pmu[j]) / sj;
                            sum += pdy[i * l + j] * hat;
                        }
                        pdg[j] = sum;
                    }).AssertCompleted();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(mode), "Invalid normalization mode");
            }
        }

        /// <summary>
        /// Calculates the gradient with respect to the beta <see cref="Tensor"/> in a batch normalization layer
        /// </summary>
        /// <param name="mode">The desired normalization mode to apply</param>
        /// <param name="info">The ifo on the input <see cref="Tensor"/> to process</param>
        /// <param name="dy">The output <see cref="Tensor"/> error delta for the current layer</param>
        /// <param name="dbeta">The resulting beta gradient <see cref="Tensor"/></param>
        public static unsafe void BatchNormalizationBackwardBeta(
            NormalizationMode mode, in TensorInfo info, in Tensor dy, in Tensor dbeta)
        {
            if (info.Size != dy.Length) throw new ArgumentException("The tensor shape doesn't match the input info", nameof(dy));
            switch (mode)
            {
                case NormalizationMode.Spatial:
                    if (!dbeta.MatchShape(1, info.Channels)) throw new ArgumentException("The beta tensor must have a value for each input channel", nameof(dbeta));
                    int
                        n = dy.Entities,
                        slice = info.SliceSize,
                        l = info.Size;
                    float* pdy = dy, pdbeta = dbeta;
                    Parallel.For(0, info.Channels, c =>
                    {
                        float bc = 0;
                        float* start = pdy + c * slice;
                        for (int i = 0; i < n; i++, start += l)
                            for (int xy = 0; xy < slice; xy++)
                                bc += start[xy];
                        pdbeta[c] = bc;
                    }).AssertCompleted();
                    break;
                case NormalizationMode.PerActivation: 
                    if (!dbeta.MatchShape(1, dy.Length)) throw new ArgumentException("The beta tensor must have a value for output feature", nameof(dbeta));
                    FullyConnectedBackwardBias(dy, dbeta); // Vertical compression
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(mode), "Invalid normalization mode");
            }
        }

        #endregion

        #region Depth concatenation

        /// <summary>
        /// Executes the forward pass on a depth stacking layer
        /// </summary>
        /// <param name="inputs">A <see cref="Span{T}"/> containing the input <see cref="Tensor"/> instances to stack</param>
        /// <param name="y">The output <see cref="Tensor"/></param>
        public static unsafe void DepthConcatenationForward(Span<Tensor> inputs, in Tensor y)
        {
            // Checks and offsets computation
            if (inputs.Length == 0) throw new ArgumentException("The inputs can't be empty", nameof(inputs));
            int
                n = y.Entities,
                count = 0;
            int* offsets = stackalloc int[inputs.Length];
            fixed (Tensor* p = &inputs.DangerousGetPinnableReference())
            {
                // Extract input info
                for (int i = 0; i < inputs.Length; i++)
                {
                    offsets[i] = count;
                    count += p[i].Length;
                    if (p[i].Entities != y.Entities) throw new ArgumentException("The number of samples must be the same for all tensors");
                }
                if (y.Length != count) throw new ArgumentException("The target tensor doesn't have the right size", nameof(y));

                // Concatenate the tensors in parallel
                float* py = y;
                Tensor* pf = p; // Local copy for closure
                void Kernel(int i)
                {
                    float*
                        psource = pf[i],
                        ptarget = py + offsets[i];
                    int l = pf[i].Length;
                    long bytes = sizeof(float) * l;
                    for (int j = 0; j < n; j++, psource += l, ptarget += count)
                        Buffer.MemoryCopy(psource, ptarget, bytes, bytes);
                }
                Parallel.For(0, inputs.Length, Kernel).AssertCompleted();
            }
        }

        /// <summary>
        /// Executes the backward pass on a depth stacking layer
        /// </summary>
        /// <param name="dy">The input <see cref="Tensor"/> with the error delta to backpropagate</param>
        /// <param name="offset">The left offset for the <see cref="Tensor"/> instance to extract</param>
        /// <param name="dx">A <see cref="Span{T}"/> with the target <see cref="Tensor"/> instances</param>
        public static unsafe void DepthConcatenationBackward(in Tensor dy, int offset, in Tensor dx)
        {
            // Checks and offsets computation
            if (dx.Length == 0) throw new ArgumentException("The result span can't be empty", nameof(dx));
            if (dy.Entities != dx.Entities) throw new ArgumentException("The number of samples must be the same for both tensors");
            if (dy.Length - offset < dx.Length) throw new ArgumentException("Invalid offset value");

            // Backpropagate in parallel
            float* pdy = dy, pdx = dx;
            int
                xl = dx.Length,
                yl = dy.Length,
                bytes = sizeof(float) * xl;
            void Kernel(int i) => Buffer.MemoryCopy(pdy + yl * i + offset, pdx + i * xl, bytes, bytes);
            Parallel.For(0, dy.Entities, Kernel);
        }

        #endregion
    }
}
