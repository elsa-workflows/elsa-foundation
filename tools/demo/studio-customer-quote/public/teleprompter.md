# Elsa Studio demo

## 1. Customer quote

_On screen: Show this slide, then open the prepared quote workflow in Studio._

I’ll walk through the prepared quote workflow. It uses a local customer API and a small calculation to return a customer and total in one response.

I’ll show the prepared runtime incident and safe API receipt. Live fault injection or Secret rotation is optional.

## 2. API access

_On screen: Open the existing customer-api Text Secret and HTTP Connection. Show Bearer authentication and the Secret selection. Keep the value hidden._

The customer-api Text Secret is already prepared. I’ll open it and show its status without revealing the value.

Then I’ll open the existing HTTP Connection and show its Bearer authentication setting and customer-api Secret reference.

The prepared quote workflow already uses this Connection to call the customer API. The token stays in the Secret store.

The demo API runs locally. The existing Connection is limited to its loopback host and uses HTTP for this local setup.

## 3. Reusable calculation

_On screen: Open the existing CalculateOrderTotal. Show its inputs, output, outcome and prepared test result for 40 + 2 = 42. Then show the published version._

This is the existing CalculateOrderTotal activity. It has two integer inputs, Subtotal and Shipping.

It adds them and returns Total. When it finishes, it reports the Calculated outcome.

Its prepared test result uses 40 and 2 and returns 42.

The prepared quote workflow uses a published version of this activity. The same calculation can be reused in other workflows.

## 4. Quote workflow

_On screen: Show the four workflow activities. Send POST /workflows/http/quotes with subtotal 40 and shipping 2. Open the completed run and select CalculateOrderTotal._

Here is the prepared quote workflow. It receives an HTTP request, looks up a customer through the Connection, calculates the total, and writes the response.

I send a POST with subtotal 40 and shipping 2. The customer ID comes from the configured lookup URL.

The response contains the customer details and a total of 42.

In the completed run, I can select the calculation and see the two inputs, the output, and the Calculated outcome. More detail is available when I need it.

## 5. Runtime diagnosis

_On screen: Optional live fault injection: change the response Body to read the missing profile.segment property, publish, and send the same request. Otherwise, show the prepared incident. Inspect the error; only if you injected the failure, restore Body, publish, and retry._

The prepared run shows a response expression reading a property this customer response doesn’t have. If I want to demonstrate this live, I can set the Body to that expression, publish, and send a request.

The expression is valid JavaScript, but it fails when the workflow evaluates it. The request doesn’t receive the expected quote response.

I open the run and inspect the incident. It points to the Body input of Write HTTP Response, with the expression and its error together.

If I injected the failure, I restore Body to use the customer ID and name that the API returns, along with the total. Then I publish and send the request again.

The prepared successful run returns HTTP 200 with a total of 42.

## 6. Secret rotation

_On screen: Optional live rotation: update customer-api to the prepared second demo value. If you do, do not republish the repaired workflow. Repeat the request and show the safe API receipt. Otherwise, show the prepared receipt._

The customer-api Secret is already prepared. If I demonstrate rotation live, I update the existing Secret and leave the repaired workflow as it is.

After a live rotation, the same request still returns the customer and a total of 42.

The local API receipt confirms that the next call used credential version two. It records the version and result, without showing the token.

The Connection picks up the updated Secret while the workflow keeps using the same published version.
