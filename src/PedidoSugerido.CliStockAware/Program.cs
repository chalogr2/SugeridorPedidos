using PedidoSugerido.Core.Application;

var startup = new CliStockAwareStartup();
var exitCode = startup.Run(args);

return exitCode;
