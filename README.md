# SugeridorPedidos

Aplicacion local en `.NET 8` para generar pedidos sugeridos de inventario a partir de archivos Excel con ventas mensuales historicas por producto.

No usa:
- web app
- base de datos
- nube
- APIs
- IA en runtime

Todo el procesamiento ocurre localmente con `ClosedXML`.

## Que hace

Lee uno o varios archivos `.xlsx` con ventas historicas por año, consolida la informacion por codigo de producto y genera un archivo Excel con 7 metodos de pedido sugerido:

1. `Gonzalez Ruiz`
2. `Promedio`
3. `Minimos`
4. `Maximos`
5. `Exp Smoothing`
6. `Estacional`
7. `Recomendacion Claude`

El archivo de salida contiene 4 hojas:

1. `Todos los Productos`
2. `Filtrado (Pedido > 0)`
3. `Filtrado Sin Máximos`
4. `Filtrado Sin Máx ni Claude`

## Requisitos

- `.NET SDK 8`
- Windows / PowerShell

Verificar:

```powershell
dotnet --version
```

## Estructura del proyecto

```text
src/PedidoSugerido.Core
src/PedidoSugerido.Cli
tests/PedidoSugerido.Tests
```

## Formato esperado de los Excel de entrada

Cada archivo:
- debe ser `.xlsx`
- normalmente representa un año
- puede tener varias hojas

La aplicacion usa la primera hoja que:
- no se llame `Control`
- no contenga `Exclu` en el nombre

Las columnas esperadas son:

1. `Codigo del producto`
2. `Nombre del producto`
3. `Modelo del producto`
4. `SaldoActual` o similar
5. `Enero`
6. `Febrero`
7. `Marzo`
8. `Abril`
9. `Mayo`
10. `Junio`
11. `Julio`
12. `Agosto`
13. `Septiembre`
14. `Octubre`
15. `Noviembre`
16. `Diciembre`
17. `TotalAnual####`

Notas:
- si una celda dice `no data`, se ignora
- si hay archivos con `pedido` en el nombre, la app los ignora para no releer salidas anteriores
- si no puede sacar el año del encabezado `TotalAnual####`, intenta leerlo del nombre del archivo

## Como identifica el año de cada Excel

El año se detecta asi, en este orden:

1. Desde la columna 17, por ejemplo `TotalAnual2025`
2. Si no existe, desde el nombre del archivo, por ejemplo `ventas_2025.xlsx`

Si no puede encontrar el año en ninguno de los dos lugares, la ejecucion falla.

## Como usa varios archivos de distintos años

Si agregas varios Excel de distintos años, la app consolida todo por `codigo de producto`.

Ejemplo:
- `ventas_2023.xlsx`
- `ventas_2024.xlsx`
- `ventas_2025.xlsx`

Para un mismo producto, la app arma un historial con:
- Enero-Diciembre 2023
- Enero-Diciembre 2024
- Enero-Diciembre 2025

El `stock` usado es el del archivo mas reciente para ese producto.

### Como calcula cada columna con varios años

`Gonzalez Ruiz`
- Calcula un resultado por cada año historico que tenga datos de los meses de cobertura.
- Si la cobertura dura mas de 12 meses, repite el mismo ciclo de meses dentro de ese mismo año historico.
- Luego promedia los resultados anuales y redondea hacia arriba.

`Promedio`
- Junta todos los meses validos de todos los años en una sola serie.
- Calcula un promedio mensual global.
- Usa ese promedio para cobertura + lead time.

`Minimos`
- Usa el menor valor mensual encontrado en toda la historia consolidada.

`Maximos`
- Usa el mayor valor mensual encontrado en toda la historia consolidada.

`Exp Smoothing`
- Ordena todos los meses cronologicamente a traves de todos los años.
- Aplica suavizamiento exponencial sobre toda la serie.

`Estacional`
- Para cada mes de cobertura, promedia ese mismo mes entre todos los años disponibles.
- Si la cobertura repite un mes por durar mas de 12 meses, ese mes se vuelve a contar.

`Recomendacion Claude`
- Calcula primero los 6 metodos anteriores.
- Luego pondera segun estacionalidad y clasificacion ABC.

## Build

Desde la raiz del repo:

```powershell
dotnet restore .\PedidoSugerido.sln
dotnet build .\PedidoSugerido.sln
```

## Ejecutar

Puedes usar fechas explicitas de cobertura o el modo legacy por indices de mes.

### Opcion recomendada: fechas explicitas

```powershell
dotnet run --project .\src\PedidoSugerido.Cli -- `
  --input-dir "C:\Ruta\A\Excels" `
  --output "C:\Ruta\Salida\pedido-sugerido.xlsx" `
  --coverage-start-date 2026-06-01 `
  --coverage-end-date 2027-06-30 `
  --delivery-date 2026-05-31 `
  --today 2026-03-20
```

Eso significa:
- cobertura desde `1 de junio de 2026`
- hasta `30 de junio de 2027`
- inclusive por meses

### Opcion legacy: indices de mes

La app sigue aceptando:

```powershell
--coverage-start 5 --coverage-end 9
```

Eso es `0-indexed`, o sea:
- `0 = enero`
- `5 = junio`
- `9 = octubre`

Ejemplo:

```powershell
dotnet run --project .\src\PedidoSugerido.Cli -- `
  --input-dir "C:\Ruta\A\Excels" `
  --output "C:\Ruta\Salida\pedido-sugerido.xlsx" `
  --coverage-start 5 `
  --coverage-end 9 `
  --delivery-date 2026-05-31 `
  --today 2026-03-20
```

Eso equivale a cobertura de `junio 2026` a `octubre 2026`.

## Ejecucion interactiva

Si no pasas todos los argumentos, la app pregunta lo que falta:

```powershell
dotnet run --project .\src\PedidoSugerido.Cli
```

## Parametros CLI

Obligatorios en la practica:

- `--input-dir`
- `--output`
- `--delivery-date`
- `--today`

Cobertura, una de estas dos opciones:

1. Recomendado
   - `--coverage-start-date`
   - `--coverage-end-date`

2. Legacy
   - `--coverage-start`
   - `--coverage-end`

## Ejemplo real

Cobertura de abril 2026 a abril 2027:

```powershell
dotnet run --project .\src\PedidoSugerido.Cli -- `
  --input-dir "C:\Users\chalo\Documents\Diseno\ExcelsPedidosSugeridos" `
  --output "C:\Users\chalo\Documents\Diseno\ResultadosPedidosSugeridos\pedido-sugerido.xlsx" `
  --coverage-start-date 2026-04-01 `
  --coverage-end-date 2027-04-30 `
  --delivery-date 2026-05-31 `
  --today 2026-03-20
```

Cobertura de junio 2026 a junio 2027:

```powershell
dotnet run --project .\src\PedidoSugerido.Cli -- `
  --input-dir "C:\Users\chalo\Documents\Diseno\ExcelsPedidosSugeridos" `
  --output "C:\Users\chalo\Documents\Diseno\ResultadosPedidosSugeridos\pedido-sugerido.xlsx" `
  --coverage-start-date 2026-06-01 `
  --coverage-end-date 2027-06-30 `
  --delivery-date 2026-05-31 `
  --today 2026-03-20
```

## Archivo de salida

La salida es un `.xlsx` con:

- todas las filas
- un filtrado con productos que tienen algun pedido > 0
- una hoja sin la columna de maximos
- una hoja sin la columna de maximos ni la recomendacion final

Tambien aplica formato visual para destacar:
- `Gonzalez Ruiz`
- `Recomendacion Claude`

## Tests

```powershell
dotnet test .\tests\PedidoSugerido.Tests\PedidoSugerido.Tests.csproj
```

## Troubleshooting

Si PowerShell marca error con argumentos tipo `--input-dir`, asegúrate de correr el comando completo, no solo una linea de argumentos suelta.

Ejemplo correcto:

```powershell
dotnet run --project .\src\PedidoSugerido.Cli -- `
  --input-dir "C:\Ruta\A\Excels" `
  --output "C:\Ruta\Salida\pedido-sugerido.xlsx" `
  --coverage-start-date 2026-06-01 `
  --coverage-end-date 2026-10-31 `
  --delivery-date 2026-05-31 `
  --today 2026-03-20
```

## Estado actual

La aplicacion esta pensada para mantenerse totalmente local y deterministica. Si cambias el `README`, no necesitas cambiar la aplicacion para usarla en su estado actual.
