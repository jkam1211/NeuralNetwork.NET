﻿// ===================================
// Credits for the base of this code
// ===================================
//
// Accord Math Library
// The Accord.NET Framework
// http://accord-framework.net
//
// Copyright © César Souza, 2009-2017
// cesarsouza at gmail.com
//
// Copyright © Jorge Nocedal, 1990
// http://users.eecs.northwestern.edu/~nocedal/
//
//    This library is free software; you can redistribute it and/or
//    modify it under the terms of the GNU Lesser General Public
//    License as published by the Free Software Foundation; either
//    version 2.1 of the License, or (at your option) any later version.
//
//    This library is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//    Lesser General Public License for more details.
//
//    You should have received a copy of the GNU Lesser General Public
//    License along with this library; if not, write to the Free Software
//    Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA

using System;
using System.Diagnostics.CodeAnalysis;
using NeuralNetworkNET.SupervisedLearning.Optimization.Abstract;
using NeuralNetworkNET.SupervisedLearning.Optimization.Dependencies;

namespace NeuralNetworkNET.SupervisedLearning.Optimization
{
    /// <summary>
    ///   Limited-memory Broyden–Fletcher–Goldfarb–Shanno (L-BFGS) optimization method
    /// </summary>
    /// 
    /// <remarks>
    /// <para>
    ///   The L-BFGS algorithm is a member of the broad family of quasi-Newton optimization
    ///   methods. L-BFGS stands for 'Limited memory BFGS'. Indeed, L-BFGS uses a limited
    ///   memory variation of the Broyden–Fletcher–Goldfarb–Shanno (BFGS) update to approximate
    ///   the inverse Hessian matrix (denoted by Hk). Unlike the original BFGS method which
    ///   stores a dense  approximation, L-BFGS stores only a few vectors that represent the
    ///   approximation implicitly. Due to its moderate memory requirement, L-BFGS method is
    ///   particularly well suited for optimization problems with a large number of variables.</para>
    /// <para>
    ///   L-BFGS never explicitly forms or stores Hk. Instead, it maintains a history of the past
    ///   <c>m</c> updates of the position <c>x</c> and gradient <c>g</c>, where generally the history
    ///   <c>m</c>can be short, often less than 10. These updates are used to implicitly do operations
    ///   requiring the Hk-vector product.</para>
    /// <para>
    ///   The framework implementation of this method is based on the original FORTRAN source code
    ///   by Jorge Nocedal (see references below). The original FORTRAN source code of L-BFGS (for
    ///   unconstrained problems) is available at http://www.netlib.org/opt/lbfgs_um.shar and had
    ///   been made available under the public domain.</para>
    /// <para>
    ///   References:
    ///   <list type="bullet">
    ///     <item><description><a href="http://www.netlib.org/opt/lbfgs_um.shar">
    ///        Jorge Nocedal. Limited memory BFGS method for large scale optimization (Fortran source code). 1990.
    ///        Available in http://www.netlib.org/opt/lbfgs_um.shar </a></description></item>
    ///     <item><description>
    ///        Jorge Nocedal. Updating Quasi-Newton Matrices with Limited Storage. <i>Mathematics of Computation</i>,
    ///        Vol. 35, No. 151, pp. 773--782, 1980.</description></item>
    ///     <item><description>
    ///        Dong C. Liu, Jorge Nocedal. On the limited memory BFGS method for large scale optimization.</description></item>
    ///    </list></para>
    /// </remarks>
    /// 
    /// <example>
    /// <para>
    ///   The following example shows the basic usage of the L-BFGS solver
    ///   to find the minimum of a function specifying its function and
    ///   gradient. </para>
    ///   
    /// <code>
    /// // Suppose we would like to find the minimum of the function
    /// // 
    /// //   f(x,y)  =  -exp{-(x-1)²} - exp{-(y-2)²/2}
    /// //
    /// 
    /// // First we need write down the function either as a named
    /// // method, an anonymous method or as a lambda function:
    /// 
    /// Func&lt;double[], double> f = (x) =>
    ///     -Math.Exp(-Math.Pow(x[0] - 1, 2)) - Math.Exp(-0.5 * Math.Pow(x[1] - 2, 2));
    /// 
    /// // Now, we need to write its gradient, which is just the
    /// // vector of first partial derivatives del_f / del_x, as:
    /// //
    /// //   g(x,y)  =  { del f / del x, del f / del y }
    /// // 
    /// 
    /// Func&lt;double[], double[]> g = (x) => new double[] 
    /// {
    ///     // df/dx = {-2 e^(-    (x-1)^2) (x-1)}
    ///     2 * Math.Exp(-Math.Pow(x[0] - 1, 2)) * (x[0] - 1),
    /// 
    ///     // df/dy = {-  e^(-1/2 (y-2)^2) (y-2)}
    ///     Math.Exp(-0.5 * Math.Pow(x[1] - 2, 2)) * (x[1] - 2)
    /// };
    /// 
    /// // Finally, we can create the L-BFGS solver, passing the functions as arguments
    /// var lbfgs = new BroydenFletcherGoldfarbShanno(numberOfVariables: 2, function: f, gradient: g);
    /// 
    /// // And then minimize the function:
    /// bool success = lbfgs.Minimize();
    /// double minValue = lbfgs.Value;
    /// double[] solution = lbfgs.Solution;
    /// 
    /// // The resultant minimum value should be -2, and the solution
    /// // vector should be { 1.0, 2.0 }. The answer can be checked on
    /// // Wolfram Alpha by clicking the following the link:
    /// 
    /// // http://www.wolframalpha.com/input/?i=maximize+%28exp%28-%28x-1%29%C2%B2%29+%2B+exp%28-%28y-2%29%C2%B2%2F2%29%29
    /// 
    /// </code>
    /// </example>
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    internal sealed partial class BoundedBroydenFletcherGoldfarbShanno : GradientOptimizationMethodBase
    {
        // those values need not be modified
        private const double stpmin = 1e-20;
        private const double stpmax = 1e20;

        // Line search parameters
        private int iterations;
        private int evaluations;
        private int corrections = 5;
        private double[] lowerBound;
        private double[] upperBound;
        private double[] work;
        double factr = 1e+5;
        double pgtol;

        #region Properties

        /// <summary>
        ///   Gets the number of iterations performed in the last
        ///   call to <see cref="GradientOptimizationMethodBase.Minimize()"/>
        /// </summary>
        /// 
        /// <value>
        ///   The number of iterations performed
        ///   in the previous optimization.</value>
        public int Iterations => iterations;

        /// <summary>
        ///   Gets or sets the maximum number of iterations
        ///   to be performed during optimization. Default
        ///   is 0 (iterate until convergence)
        /// </summary>
        public int MaxIterations { get; set; }

        /// <summary>
        ///   Gets the number of function evaluations performed
        ///   in the last call to <see cref="GradientOptimizationMethodBase.Minimize()"/>
        /// </summary>
        /// 
        /// <value>
        ///   The number of evaluations performed
        ///   in the previous optimization
        /// </value>
        public int Evaluations => evaluations;

        /// <summary>
        ///   Gets or sets the number of corrections used in the L-BFGS
        ///   update. Recommended values are between 3 and 7. Default is 5
        /// </summary>
        public int Corrections
        {
            get => corrections;
            set => corrections = value <= 0 ? throw new ArgumentException(nameof(Corrections), "Number of corrections should be higher than zero") : value;
        }

        /// <summary>
        ///   Gets or sets the upper bounds of the interval
        ///   in which the solution must be found
        /// </summary>
        public double[] UpperBounds
        {
            get => upperBound;
            set => upperBound = value.Length != upperBound.Length ? throw new ArgumentException(nameof(UpperBounds), "The bounds vector should have the same length as the number of variables to be optimized") : value;
        }

        /// <summary>
        ///   Gets or sets the lower bounds of the interval
        ///   in which the solution must be found
        /// </summary>
        public double[] LowerBounds
        {
            get => lowerBound;
            set => lowerBound = value.Length != lowerBound.Length ? throw new ArgumentException(nameof(LowerBounds), "The bounds vector should have the same length as the number of variables to be optimized") : value;
        }

        /// <summary>
        ///   Gets or sets the accuracy with which the solution
        ///   is to be found. Default value is 1e5. Smaller values
        ///   up until zero result in higher accuracy.
        /// </summary>
        /// 
        /// <remarks>
        /// <para>
        ///   The iteration will stop when</para>
        /// <code>
        ///          (f^k - f^{k+1})/max{|f^k|,|f^{k+1}|,1} &lt;= factr*epsmch </code>
        /// <para>
        ///   where epsmch is the machine precision, which is automatically
        ///   generated by the code. Typical values for this parameter are:
        ///   1e12 for low accuracy; 1e7 for moderate accuracy; 1e1 for extremely
        ///   high accuracy
        /// </para>
        /// </remarks>
        public double FunctionTolerance
        {
            get => factr;
            set => factr = value < 0 ? throw new ArgumentException(nameof(FunctionTolerance), "Tolerance must be greater than or equal to zero") : value;
        }

        /// <summary>
        ///   Gets or sets a tolerance value when detecting convergence 
        ///   of the gradient vector steps. Default is 0
        /// </summary>
        /// 
        /// <remarks>
        ///   On entry pgtol >= 0 is specified by the user.  The iteration
        ///   will stop when
        /// <code>
        ///   max{|proj g_i | i = 1, ..., n} &lt;= pgtol
        /// </code>
        /// <para>
        ///   where pg_i is the ith component of the projected gradient
        /// </para>
        /// </remarks>
        public double GradientTolerance
        {
            get => pgtol;
            set => pgtol = value;
        }

        /// <summary>
        ///   Get the exit code returned in the last call to the
        ///   <see cref="GradientOptimizationMethodBase.Minimize"/> method
        /// </summary>
        public BoundedBroydenFletcherGoldfarbShannoStatus Status { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        ///   Creates a new instance of the L-BFGS optimization algorithm
        /// </summary>
        /// 
        /// <param name="numberOfVariables">The number of free parameters in the optimization problem</param>
        public BoundedBroydenFletcherGoldfarbShanno(int numberOfVariables) : base(numberOfVariables)
        {
            upperBound = new double[numberOfVariables];
            lowerBound = new double[numberOfVariables];

            for (int i = 0; i < upperBound.Length; i++)
                lowerBound[i] = double.NegativeInfinity;

            for (int i = 0; i < upperBound.Length; i++)
                upperBound[i] = double.PositiveInfinity;
        }

        #endregion


        /// <summary>
        ///   Implements the actual optimization algorithm. This
        ///   method should try to minimize the objective function.
        /// </summary>
        protected override bool Optimize()
        {
            if (Function == null)
                throw new InvalidOperationException("function");

            if (Gradient == null)
                throw new InvalidOperationException("gradient");

            CheckGradient(Gradient, Solution);


            int n = NumberOfVariables;
            int m = corrections;

            String csave = "";
            bool[] lsave = new bool[4];
            int iprint = 101;
            int[] nbd = new int[n];
            int[] iwa = new int[3 * n];
            int[] isave = new int[44];
            double f = 0.0d;
            double[] x = new double[n];
            double[] l = new double[n];
            double[] u = new double[n];
            double[] g = new double[n];
            double[] dsave = new double[29];

            int totalSize = 2 * m * n + 11 * m * m + 5 * n + 8 * m;

            if (work == null || work.Length < totalSize)
                work = new double[totalSize];
            
            for (int i = 0; i < UpperBounds.Length; i++)
            {
                bool hasUpper = !double.IsInfinity(UpperBounds[i]);
                bool hasLower = !double.IsInfinity(LowerBounds[i]);
                if (hasUpper && hasLower)
                    nbd[i] = 2;
                else if (hasUpper)
                    nbd[i] = 3;
                else if (hasLower)
                    nbd[i] = 1;
                else nbd[i] = 0; // unbounded

                if (hasLower)
                    l[i] = LowerBounds[i];
                if (hasUpper)
                    u[i] = UpperBounds[i];
            }


            // We now define the starting point.
            {
                for (int i = 0; i < n; i++)
                    x[i] = Solution[i];
            }

            // Task initialization
            string task = "START";
            iterations = 0;

            // Main loop
            while (true)
            {
                // Check completion
                double newF;
                double[] newG;
                if (Token.IsCancellationRequested || MaxIterations > 0 && iterations >= MaxIterations)
                {
                    Exit(x, out newF, out newG);
                    return true;
                }
                iterations++;

                // This is the call to the L-BFGS-B code
                Setulb(n, m, x, 0, l, 0, u, 0, nbd, 0, ref f, g, 0,
                    factr, pgtol, work, 0, iwa, 0, ref task, iprint, ref csave,
                    lsave, 0, isave, 0, dsave, 0);

                // Normal iteration
                if (task.StartsWith("FG", StringComparison.OrdinalIgnoreCase))
                {
                    newF = Function(x);
                    newG = Gradient(x);
                    evaluations++;
                    f = newF;
                    for (int j = 0; j < newG.Length; j++)
                        g[j] = newG[j];
                }

                // Final tests
                else if (task.StartsWith("NEW_X", StringComparison.OrdinalIgnoreCase))
                {

                }
                else
                {
                    if (task == "ABNORMAL_TERMINATION_IN_LNSRCH")
                        Status = BoundedBroydenFletcherGoldfarbShannoStatus.LineSearchFailed;
                    else if (task == "CONVERGENCE: REL_REDUCTION_OF_F_<=_FACTR*EPSMCH")
                        Status = BoundedBroydenFletcherGoldfarbShannoStatus.FunctionConvergence;
                    else if (task == "CONVERGENCE: NORM_OF_PROJECTED_GRADIENT_<=_PGTOL")
                        Status = BoundedBroydenFletcherGoldfarbShannoStatus.GradientConvergence;
                    else throw new InvalidOperationException(task);
                    Exit(x, out newF, out newG);
                    return true;
                }
            }
        }

        // Save the current progress
        private void Exit(double[] x, out double newF, out double[] newG)
        {
            for (int j = 0; j < Solution.Length; j++)
                Solution[j] = x[j];
            newF = Function(x);
            newG = Gradient(x);
            evaluations++;
        }
    }
}
