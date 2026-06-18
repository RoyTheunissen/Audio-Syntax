using System;

namespace RoyTheunissen.AudioSyntax
{
    /// <summary>
    /// Base class for the container class that contains all the parameterless audio event configs data.
    /// The Audio Syntax system needs to have access to this container at runtime to be able to play parameterless
    /// audio events, but it is generated code so Audio Syntax cannot reference it directly. You can access it via
    /// reflection, but that is very slow. We are instead opting to have it register itself explicitly.
    /// </summary>
    public abstract class AudioParameterlessEventsContainer
    {
        private static Type containerType;
        public static Type ContainerType => containerType;

        protected static void RegisterContainerType<T>()
        {
            containerType = typeof(T);
        }
    }
}
