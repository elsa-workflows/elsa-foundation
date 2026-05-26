namespace Elsa3.Activities.Design.Import.Contracts;

public interface IActivityCollectionJsonSource
{
    /// <summary>
    /// Opens a UTF-8 Json Stream that contains a list of workflow definitions. 
    /// The format of the json should be an array of workflow definitions, where each object has the same format as the one produced by the <see cref="Elsa3.Models.Elsa3WorkflowDefinition"/> class.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Stream> OpenStream(CancellationToken cancellationToken);
}
