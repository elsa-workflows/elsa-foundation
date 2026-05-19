namespace Elsa.Primitives.Contracts
{
    public interface ITypeInformation
    {
        /// <summary>
        /// The type name.
        /// </summary>
        string TypeName { get; }

        /// <summary>
        /// The namespace where the type lives.
        /// </summary>
        string Namespace { get; }

        /// <summary>
        /// The name of the assembly where the type lives
        /// </summary>
        string AssemblyName { get; }

        /// <summary>
        /// The version of the assembly where the type lives
        /// </summary>
        string? AssemblyVersion { get; }
    }
}
