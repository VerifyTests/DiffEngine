# Diff Tool Order


## Default

include: defaultOrder


## Custom order


### ViaEnvironment Variable

Set an `DiffEngine.ToolOrder` environment variable with the preferred order of toll resolution. The value can be comma (`,`), pipe (`|`), or space separated.

For example `VisualStudio,Meld` will result in VisualStudio then Meld then all other tools being the order.


### Via Code

snippet: UseOrder