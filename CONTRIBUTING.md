# Contribuir a Trazo

Las correcciones, mejoras y sugerencias son bienvenidas. Antes de un cambio grande, abre un issue para acordar su alcance.

## Preparar el entorno

Trabaja en Windows con Git y .NET Framework 4.8. Desde la raíz del repositorio:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

El ejecutable de desarrollo queda en `dist/windows/app/Trazo.exe` y las pruebas en `dist/windows/tests/`. Los scripts utilizan el compilador C# incluido con .NET Framework y no descargan paquetes. Abre `Trazo.sln` para trabajar con el SDK de .NET 9 y Windows Forms.

## Generar los paquetes de Windows

```powershell
.\scripts\package.ps1
.\packaging\windows\build-installer.ps1 -CompilerPath "C:\ruta\a\ISCC.exe"
```

El ZIP portátil y el instalador se generan en `dist/windows/`. Las pruebas y herramientas del instalador no deben usar capturas personales ni modificar preferencias de una instalación real.

## Organización

| Carpeta | Responsabilidad |
| --- | --- |
| `src/Trazo.Windows/App/` | Inicio, preferencias y atajos |
| `src/Trazo.Windows/Capture/` | Detección de ventanas e historial |
| `src/Trazo.Windows/Editor/` | Dibujo, anotaciones, efectos y proyectos |
| `src/Trazo.Windows/Platform/` | Integración con Windows |
| `src/Trazo.Windows/UI/` | Ventanas, configuración y bandeja |
| `src/Trazo.Windows/Properties/` | Versión y manifiesto de la aplicación |
| `scripts/` | Compilación, pruebas y paquete portátil |
| `packaging/windows/` | Asistente de instalación y recursos |
| `tests/Trazo.Windows.Tests/` | Pruebas automáticas y generadores de vistas previas |
| `docs/` | Imágenes y documentación |
| `dist/windows/` | Distribuciones de la versión vigente y salidas de desarrollo |

La implementación actual de Windows está agrupada en su propio proyecto. Las futuras plataformas podrán tener sus proyectos en `src/`, su empaquetado en `packaging/` y sus distribuciones en `dist/`.

## Validar un cambio

La suite comprueba recorte, geometría, anotaciones, historial, proyectos, efectos, atajos y configuración. Añade pruebas cuando el cambio afecte a esos comportamientos y comprueba manualmente los flujos de escritorio que modifiques.

En el pull request explica el problema, el resultado del cambio y cómo lo verificaste. Incluye una captura si cambias la interfaz.

Mantén los cambios centrados en el problema que resuelven y evita incluir archivos generados. Las contribuciones se publican bajo la licencia MIT del proyecto.
