# Contract: WriteHttpResponse body

Activity type `Elsa.Activities.Http.WriteHttpResponse`; input key `Body`; type alias `System.Text.Json.JsonElement?`
(was `String`).

## Authoring

| Binding | Example | Bound element |
|---|---|---|
| Literal | `hello`, `{"a":1}` | `String` (text, never parsed) |
| JavaScript returning a string | `return 'hi';` | `String` |
| JavaScript returning an object/array/number/boolean | `return { a: 1 };` | `Object` / `Array` / `Number` / `True`/`False` |
| JavaScript returning `null` | `return null;` | none |

## HTTP response

See the delivery rules in [data-model.md](../data-model.md#delivery-rules). Examples:

| Body | ContentType | Sent |
|---|---|---|
| `{ a: 1, B: [true] }` | unset | `200`, `application/json`, `{"a":1,"B":[true]}` |
| `"hello"` | unset | `200`, `text/plain`, `hello` |
| `"{\"a\":1}"` (text) | `application/json` | `200`, `application/json`, `{"a":1}` |
| `[1,2]` | `application/json; charset=utf-8` | `200`, the factory's type, `[1,2]` |
| `{ a: 1 }` | unset, status 204 | `204`, no body |

## Response instruction (committed result)

`{ "statusCode": 200, "headers": {}, "body": { "a": 1 }, "contentType": null }`. A committed instruction from before this
change, `"body": "text"`, is delivered unchanged.
