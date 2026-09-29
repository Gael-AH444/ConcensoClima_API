# 🌤️ ConsensoClima

> **¿Puedo confiar en este dato del clima para decidir?**
> Una app de consola en C# que consulta varias fuentes de clima *al mismo tiempo*, las consolida en una sola lectura y entrega una **señal de confianza** según qué tanto coinciden — sin caerse aunque una fuente falle.

![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Tests](https://img.shields.io/badge/tests-xUnit%20%2B%20Moq-brightgreen)
![License](https://img.shields.io/badge/license-MIT-blue)

---

## 🎯 El problema

El clima de **una sola API** es un punto único de falla y de error: los proveedores no siempre coinciden, y cualquier API se cae o se pone lenta. Quien toma decisiones con base en el clima —logística, eventos, obra, agro— no tiene una forma fácil de saber si un dato es confiable.

**ConsensoClima** consulta **varias fuentes independientes a la vez** por ciudad y entrega:

- una **lectura consolidada** (promedio y rango entre fuentes), y
- un **nivel de acuerdo** (Alto / Medio / Bajo) que responde qué tanto coinciden las fuentes,

y **sobrevive** a que una fuente falle o tarde, reportándolo en lugar de tumbar la corrida.

---

## 🖥️ Ejemplo de salida

```
══ Querétaro ══
  ✓ Open-Meteo  :  24.3 °C
  ✓ MET Norway  :  23.8 °C
  Promedio : 24.1 °C  (rango 23.8…24.3, amplitud 0.5)
  Acuerdo  : Alto  (2/2 fuentes)

══ Ciudad de México ══
  ✓ Open-Meteo  :  19.1 °C
  ✗ MET Norway  : timeout (> 5s)
  Promedio : 19.1 °C  (rango 19.1…19.1, amplitud 0.0)
  Acuerdo  : Alto  (1/2 fuentes)
```

Fíjate en la segunda ciudad: una fuente hizo *timeout*, pero la corrida **siguió** y reportó el estado de cada fuente. Ése es el corazón del proyecto.

---

## ✨ Características

- **Consulta concurrente** de 2+ fuentes de clima por ciudad (patrón *fan-out* con `Task.WhenAll`).
- **Consolidación** por ciudad: promedio, rango (mín/máx) y **nivel de acuerdo** como señal de confianza.
- **Resiliencia**: *timeout* por fuente y captura de fallos individuales — una fuente caída o lenta **no** tumba la corrida.
- **Reporte por fuente**: qué respondió, qué falló y por qué.
- **Lista de ciudades configurable** desde `appsettings.json`, sin recompilar.
- **Fuentes intercambiables** detrás de una interfaz común: sumar un proveedor nuevo no toca la lógica de negocio.
- **Cero llaves para arrancar**: usa Open-Meteo y MET Norway, ambas gratuitas y sin registro.

---

## 🧩 Cómo funciona

```
Ciudades (config)
      │
      ▼
 Geocodificador ──► nombre → coordenadas (lat/lon)
      │
      ▼
   Agregador ──► [ Open-Meteo | MET Norway | … ]  ← en paralelo, con timeout por fuente
      │
      ▼
 Consolidador ──► promedio · rango · nivel de acuerdo   (LINQ)
      │
      ▼
   Reporte de consola
```

Cada proveedor es un **adaptador** que traduce el JSON particular de su API a un modelo común del dominio (`LecturaClima`). El agregador orquesta la concurrencia y la resiliencia sin conocer ninguna API concreta: solo habla con la interfaz `IProveedorClima`.

---

## 🛠️ Stack tecnológico

- **C# 12** sobre **.NET 8+**
- **Generic Host** (`Microsoft.Extensions.Hosting`) → DI, configuración y logging de una pieza
- **`IHttpClientFactory`** con *typed clients* (`Microsoft.Extensions.Http`)
- **`System.Text.Json`** para deserialización (integrado, sin paquetes extra)
- **Options pattern** (`IOptions<T>`) para configuración tipada
- **`ILogger`** con logging estructurado
- **xUnit + Moq** para las pruebas

Fuentes de clima: [Open-Meteo](https://open-meteo.com/) y [MET Norway (api.met.no)](https://api.met.no/), ambas gratuitas y sin API key.

---

## 🧪 Tests

```bash
dotnet test
```

Las pruebas corren **sin tocar internet**: los mocks (Moq) sustituyen las fronteras de I/O. Cubren:

- **Consolidación** (`Consolidador`): promedio, rango y clasificación del acuerdo, incluyendo las **fronteras** exactas de los umbrales.
- **Resiliencia** (`AgregadorClima`): un proveedor mockeado que falla — se verifica que la corrida **sobrevive** y reporta el fallo. Es el criterio de aceptación #3 vuelto código ejecutable.

---

## 🧠 Conceptos y decisiones de diseño

Este proyecto se construyó para ejercitar, con un caso realista, las bases de C#. Lo interesante es que **cada requisito de negocio "jaló" un concepto de forma natural**:

| Requisito | Concepto de C# |
|---|---|
| Consultar varias fuentes al mismo tiempo | `async/await` + `Task.WhenAll` (concurrencia de I/O) |
| Fuentes intercambiables | Interfaces + polimorfismo (POO) |
| El agregador usa las fuentes que existan | Inyección de `IEnumerable<IProveedorClima>` (DI) |
| Promedio, rango y acuerdo | LINQ de agregación (`Average`, `Min`, `Max`) |
| Modelar lecturas y consenso | `record` / value objects |
| Sobrevivir a fuente caída o lenta | `CancellationToken` + timeouts + captura por fuente |
| Ciudades configurables | `IConfiguration` + Options pattern |
| Probar sin llamar a internet | Interfaces → mocks con xUnit + Moq |

Algunas decisiones que vale la pena resaltar:

- **Excepción → dato.** Un fallo de una fuente no se propaga como excepción (que rompería el lote entero en `Task.WhenAll`); se convierte en un *resultado* (`ResultadoFuente.Falla`) que fluye y se reporta. Robustez por diseño.
- **Concurrencia de I/O ≠ paralelismo de CPU.** Mientras se esperan las respuestas HTTP no hay ningún hilo trabajando; `await` libera el hilo. No se usa `Task.Run` porque para I/O no aporta nada.
- **Testeabilidad diseñada, no añadida.** Todo el I/O vive detrás de interfaces (`IProveedorClima`, `IGeocodificador`), y la lógica de consenso es una función pura. Por eso los tests corren sin red y de forma determinista.
- **`internal` por defecto.** Nada se expone como `public` sin necesidad; encapsulación como higiene.
