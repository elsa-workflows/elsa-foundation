using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Request identity over the owner's existing serialized state, not a value-serialization profile.</summary>
internal static class ActivityDataSelectionSnapshot
{
    internal static string Hash<T>(T value)
    {
        return "sha256:" + Elsa.Serialization.Core.JsonHashInput.Hash(value);
    }

    internal static ActivityDefinitionDraftState Read(JsonElement element)
    {
        var options = ActivitiesDesignJsonOptions.Create();
        options.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
        options.RespectRequiredConstructorParameters = true;
        var state = element.Deserialize<ActivityDefinitionDraftState>(options) ?? throw new JsonException();
        if (state.Contract is null || state.Provider is null || state.Options is null ||
            state.Contract.Inputs is null || state.Contract.Outputs is null || state.Contract.Outcomes is null)
            throw new JsonException();
        return state;
    }

}
