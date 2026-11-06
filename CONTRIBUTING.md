# Contribuir a Trazo

Las correcciones, mejoras y sugerencias son bienvenidas. Antes de un cambio grande, abre un issue para acordar su alcance.

## Preparar el entorno

Trabaja en Windows con Git y .NET Framework 4.8. Desde la raíz del repositorio:

```powershell
.\build.ps1
.\test.ps1
```

El ejecutable portátil queda en `dist/Trazo.exe`. Los scripts utilizan el compilador C# instalado con .NET Framework y no descargan paquetes. El archivo `Trazo.csproj` permite trabajar también con el SDK de .NET 9 y Windows Forms.

## Validar un cambio

La suite comprueba recorte, geometría, anotaciones, historial, proyectos, efectos y atajos. Añade pruebas cuando el cambio afecte a esos comportamientos y comprueba manualmente los flujos de escritorio que modifiques.

En el pull request explica el problema, el resultado del cambio y cómo lo verificaste. Incluye una captura si cambias la interfaz.

## Dónde empezar

| Archivo | Función |
| --- | --- |
| `MainForm.cs` y `Native.cs` | Ventana principal, bandeja y atajos globales |
| `WindowDetection.cs` | Selección de ventanas y controles |
| `OverlayForm.cs` y `EditorFeatures.cs` | Editor, herramientas y operaciones |
| `Annotation.cs` | Dibujo y transformación de marcas |
| `ProjectStore.cs` y `ImageEffects.cs` | Proyectos editables y exportación |
| `tests/` | Comprobaciones automáticas y vista previa |

Mantén los cambios centrados en el problema que resuelven y evita incluir archivos generados. Las contribuciones se publican bajo la licencia MIT del proyecto.
