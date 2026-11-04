# Quickstart: check the baseline by hand

1. Run a workflow whose output `OrderTotal` is the JSON `{"CustomerId":1,"Name":"René","N":1.0}`.
2. Read the instance through the Runtime API. The key is `OrderTotal`, and the value text is byte-identical
   (`é`, lowercase, and `1.0` kept).
3. Save a workflow definition whose variable default has inner whitespace and `<`, read it back, and export it
   to Git. The default text is unchanged in the API response and in the Git file.
4. Run the architecture suite after adding `DictionaryKeyPolicy = JsonNamingPolicy.CamelCase` to any options in
   `src`. `JsonBaselineArchitectureTests` fails and names the file. Remove the setting again.
