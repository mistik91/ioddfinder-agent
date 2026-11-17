# Upstream service and data

IODDfinder Agent is an independent client for the IO-Link Community's IODDfinder. It is not endorsed by PNO, IO-Link or device manufacturers.

The client requests the live public service at `https://ioddfinder.io-link.com/api/`. It uses paged `/drivers` search, `/drivers/vendors`, `/entrypage/search`, product-variant details and viewer endpoints. ZIP downloads use `/vendors/{vendorId}/iodds/{ioddId}/files/zip/rated`, the route used by the website and several public clients. No credentials are configured for these public routes. A different `/files/zip/` integration route requires an API key and is not used here.

Observed behavior is not a guaranteed upstream contract. A change in authentication, schemas, limits or availability can affect the client. The service's access conditions and the manufacturers' rights apply independently of this repository's MIT software license. The software license does not grant rights to redistribute third-party IODDs. See [IODDfinder](https://io-link.com/community/services/ioddfinder) and its [terms](https://ioddfinder.io-link.com/termsofuse).

Search, details and inspection go online on each request. There is no local catalog, bulk crawl or initial synchronization. Explicitly saved IODD ZIPs are original user downloads, identified by vendor ID, IODD ID and SHA-256. Viewer responses are transient. Upstream response headers do not provide a persistent HTTP cache contract; the managed download library is separate from HTTP caching.

The application sends search terms and requested device/package identities to IODDfinder. It has no application telemetry or hosted backend. A connected agent host can receive tool arguments, results and local paths. GitHub processes release downloads; the CLI itself does not contact GitHub during normal catalog use.

Public code references inspected during development:

- [IOLink.NET public client](https://github.com/domdeger/IOLink.NET/blob/main/src/IOLink.NET.IODD/Provider/IODDFinderPublicClient.cs)
- [UMH SensorConnect](https://github.com/united-manufacturing-hub/benthos-umh/blob/staging/sensorconnect_plugin/ioddfinder.go)
- [Node-RED IO-Link suite](https://github.com/blanpa/node-red-contrib-iolink-suite/blob/main/lib/iodd/finder.js)

No source from those clients was incorporated into the application. Research response snapshots and downloaded manufacturer files are excluded from this repository and its releases.
