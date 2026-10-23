namespace Elsa.Serialization.Core;

public interface ITypeConverter
{
    Type TargetType { get; set; }

    object? Convert(object? value);
}