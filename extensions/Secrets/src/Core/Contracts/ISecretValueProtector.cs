namespace Elsa.Secrets.Core.Contracts;

public interface ISecretValueProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
