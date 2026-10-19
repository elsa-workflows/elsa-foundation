# Prepared demo walkthrough

Use the existing resources by name; do not create or reset them during the presentation. For a fresh environment, follow the package README first. Ports are the documented local demo defaults; resource and run IDs belong to your environment.

1. Open [Secrets](http://localhost:5589/security/secrets) and show `customer-api` (active), keeping the value hidden. Open [Connections](http://localhost:5589/security/connections) and show **Customer API**: HTTP, Bearer, Ready.
2. In [Activity definitions](http://localhost:5589/workflows/activity-definitions), open **CalculateOrderTotal**. Show integer inputs Subtotal and Shipping, integer output Total, and outcome Calculated. The expression is `args.Subtotal + args.Shipping`. Show the published version and an activity test using 40 and 2.
3. In [Workflows](http://localhost:5589/workflows/definitions), open **Customer quote with protected lookup**. Show HTTP Endpoint, Send HTTP Request, CalculateOrderTotal and Write HTTP Response.
4. Send POST `/workflows/http/quotes` with `{"subtotal":40,"shipping":2}` from the HTTP tester or [API cockpit](http://127.0.0.1:5582/cockpit). Expect HTTP 200: `{"customerId":"C-1042","name":"Alex Morgan","total":42}`. Open the completed run and inspect the calculator inputs and output.
5. For diagnosis, use a saved failed run from your environment. The optional failure expression reads the missing `profile.segment` on the customer response. Its incident should identify Write HTTP Response / Body. If you inject this live, restore the working Body, publish and retest before continuing.
6. Explain rotation with the [rotation slide](slides.html#rotation). Live rotation is optional. Update the existing Secret and send the same request without republishing the workflow. Check accepted authentication in the cockpit. The slide's screenshot is a historical receipt.

[Slides](slides.html) · [Teleprompter](teleprompter.html)

## API cockpit

Click **Send quote request** with subtotal 40 and shipping 2. Show the API call, accepted Bearer authentication and workflow total 42. Expand **Request and response details** to show the custom request header and customer response. Keys are masked; **Reveal demo key** temporarily shows only a recognized disposable demo key.

The cockpit's v1/v2 labels name the API's two configured demo keys. They are not Secret store version numbers. Use the actual Secret version shown by your environment.
