# Quickstart: check the value model by hand

Run against a Workbench host built from the initiative branch with this spec implemented.

1. **A read changes nothing.** Create a workflow with a JSON variable `order` =
   `{"id": 9007199254740993, "price": 10.50}`. Add a RunJavaScript with `return variables.order.price * 2;`. Run it.
   The stored `order` text is unchanged and the result is `21`.
2. **A lossy write is refused.** Change the script to `return { id: variables.order.id };`. Run it. The step fails;
   the instance diagnostic names `id`, stored `9007199254740993`, written `9007199254740992`, and says the number
   exceeds `9007199254740991`.
3. **A string passes.** Store the id as `"9007199254740993"`. Run step 2 again. It passes.
4. **A member change keeps the rest.** Use `variables.order.price = 20;`. Run it. Only `price` changed; `id` is still
   `9007199254740993`.
5. **One dynamic kind.** Open Studio's variable type list. It offers JSON, not Object.
6. **Headers.** Bind `WriteHttpResponse.Headers` to `{"X-Test": ["1"]}`, publish and call the endpoint. The response
   has `X-Test: 1`.
7. **Elsa 3.** Import fixture `reusable-reads-result`, publish and run both branches.
