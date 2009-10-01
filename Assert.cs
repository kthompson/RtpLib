using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace RtpLib
{
    /// <summary>
    /// Provides static methods for common code debugging tools
    /// </summary>
    public static class Assert
    {

        #region Public Methods 

        /// <summary>
        /// Stops the debugger if attached and does nothing with the exception
        /// Simply used to keep the compiler from complaining about an unused variable
        /// </summary>
        /// <param name="ex"></param>
        [System.Diagnostics.DebuggerHidden]
        public static void Suppress(Exception ex)
        {
            Break();
        }

        /// <summary>
        /// Stops the debugger if attached
        /// </summary>
        [System.Diagnostics.DebuggerHidden]
        public static void Break()
        {
            if (System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
        }

        /// <summary>
        /// Generic assertion that will throw whatever exception you specify if the expression is false
        /// </summary>
        /// <param name="expression">Expression that evaluates to a boolean to check</param>
        /// <param name="exceptionCreator"></param>
        [System.Diagnostics.DebuggerHidden]
        public static void That(bool expression, Func<Exception> exceptionCreator)
        {
            if (!expression)
                Throw(exceptionCreator);
        }

        /// <summary>
        /// Generic assertion that will throw whatever exception you specify if the expression is false
        /// </summary>
        /// <param name="expression">Expression that evaluates to a boolean to check</param>
        /// <param name="exceptionCreator"></param>
        [System.Diagnostics.DebuggerHidden]
        public static void IsNot(bool expression, Func<Exception> exceptionCreator)
        {
            if (expression)
                Throw(exceptionCreator);
        }

        /// <summary>
        /// Throws an ArgumentNullException if the argument is null
        /// </summary>
        /// <typeparam name="T">Class</typeparam>
        /// <param name="arg">Value to validate</param>
        /// <param name="argName">Name of the argument being validated</param>
        /// <exception cref="System.ArgumentNullException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void IsNotNull<T>(T arg, string argName)
            where T : class
        {
            if (arg == null)
                Throw(() => new ArgumentNullException(argName));
        }

        /// <summary>
        /// Throws an ArgumentNullException if the value does not equal arg
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arg"></param>
        /// <param name="value"></param>
        /// <param name="argName"></param>
        /// <exception cref="System.ArgumentException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void AreEqual<T>(T arg, T value, string argName)
        {
            if (!Equals(arg, value))
                Throw(() => new ArgumentException(argName));
        }

        /// <summary>
        /// Throws an ArgumentOutOfRangeException if the argument is not in the list of valid values
        /// </summary>
        /// <typeparam name="T">Struct</typeparam>
        /// <param name="arg">Value to be validated</param>
        /// <param name="argName">Name of the argument being validated</param>
        /// <param name="values"></param>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void IsOneOf<T>(T arg, string argName, params T[] values)
            where T : struct, IComparable
        {
            foreach (var value in values)
                if (Equals(arg, value))
                    return;

            Throw(() => new ArgumentOutOfRangeException(argName));
        }

        /// <summary>
        /// Throws an ArgumentOutOfRangeException if the argument is not within the specified range
        /// </summary>
        /// <typeparam name="T">Struct</typeparam>
        /// <param name="arg">Value to be validated</param>
        /// <param name="argName">Name of the argument being validated</param>
        /// <param name="minValue">Minimum value anything lower throws exception</param>
        /// <param name="maxValue">Maximum value anything higher throws exception</param>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void IsBetween<T>(T arg, string argName, T minValue, T maxValue)
            where T : struct, IComparable
        {
            if (arg.CompareTo(minValue) < 0 || arg.CompareTo(maxValue) > 0)
                Throw(() => new ArgumentOutOfRangeException(argName));
        }

        /// <summary>
        /// Throws an ArgumentOutOfRangeException if the argument is not greater than specified value
        /// </summary>
        /// <typeparam name="T">Struct</typeparam>
        /// <param name="arg">Value to be validated</param>
        /// <param name="argName">Name of the argument being validated</param>
        /// <param name="value">Value the argument must be greater than in order to not throw</param>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void IsGreaterThan<T>(T arg, string argName, T value)
            where T : struct, IComparable
        {
            if (arg.CompareTo(value) <= 0)
                Throw(() => new ArgumentOutOfRangeException(argName));
        }

        /// <summary>
        /// Throws an ArgumentOutOfRangeException if the argument is not greater than specified value
        /// </summary>
        /// <typeparam name="T">Struct</typeparam>
        /// <param name="arg">Value to be validated</param>
        /// <param name="argName">Name of the argument being validated</param>
        /// <param name="value">Value the argument must be greater than in order to not throw</param>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void IsGreaterThanEqual<T>(T arg, string argName, T value)
            where T : struct, IComparable
        {
            if (arg.CompareTo(value) < 0)
                Throw(() => new ArgumentOutOfRangeException(argName));
        }

        /// <summary>
        /// Throws an ArgumentOutOfRangeException if the argument is not less than specified value
        /// </summary>
        /// <typeparam name="T">Struct</typeparam>
        /// <param name="arg">Value to be validated</param>
        /// <param name="argName">Name of the argument being validated</param>
        /// <param name="value">Value the argument must be less than in order to not throw</param>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void IsLessThan<T>(T arg, string argName, T value)
            where T : struct, IComparable
        {
            if (arg.CompareTo(value) >= 0)
                Throw(() => new ArgumentOutOfRangeException(argName));
        }

        /// <summary>
        /// Throws an ArgumentOutOfRangeException if the argument is not less than specified value
        /// </summary>
        /// <typeparam name="T">Struct</typeparam>
        /// <param name="arg">Value to be validated</param>
        /// <param name="argName">Name of the argument being validated</param>
        /// <param name="value">Value the argument must be less than in order to not throw</param>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        [System.Diagnostics.DebuggerHidden]
        public static void IsLessThanEqual<T>(T arg, string argName, T value)
            where T : struct, IComparable
        {
            if (arg.CompareTo(value) > 0)
                Throw(() => new ArgumentOutOfRangeException(argName));
        }

        #endregion

        #region Internal Methods 

        /// <summary>
        /// Stops the debugger if attached and throws the exception
        /// </summary>
        /// <param name="ex"></param>
        [System.Diagnostics.DebuggerHidden]
        private static void Throw(Func<Exception> ex)
        {
            Break();
            throw ex();
        }

        #endregion

    }
}