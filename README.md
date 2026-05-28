# Albion App

Herramienta de gestión para guilds de **Albion Online**. Organiza composiciones, planifica eventos y monitorea jugadores en tiempo real capturando el tráfico del juego.

---

## Características

### Composiciones (Builds)
- Crea y edita builds con todos los slots de equipo: arma principal, offhand, cabeza, pecho, botas, capa, montura, bolsa, pociones, comida y extras
- Selector de ítems con búsqueda en tiempo real por nombre (multi-idioma)
- Imágenes de ítems cargadas automáticamente desde los servidores de Albion

### Grupos de composición
- Agrupa múltiples builds en composiciones de grupo (ej: "Comp ZvZ", "Comp Dungeons")
- Asigna emojis personalizados a cada rol usando el picker integrado
- **Exporta el grupo como imagen** (copia al portapapeles o guarda como PNG) — ideal para compartir en Discord

### Eventos de guild
- Crea plantillas de eventos (raids, ZvZ, dungeons, etc.)
- Activa eventos con descripción personalizada y fecha/hora
- Historial de eventos activados
- **Integración con Discord Bot**: publica eventos automáticamente en un canal con botones interactivos o reacciones

### Jugador / Destiny Board
- Detecta automáticamente al jugador al conectarse al juego (captura de red)
- Muestra el progreso del Destiny Board con todos los logros y porcentajes

### Calculadora de crafteo
- Calcula el costo de crafteo considerando bonificaciones por ciudad, retorno de materiales y diarios
- Workspace con múltiples pestañas persistentes entre sesiones

### Configuración
- Selección de idioma para los nombres de ítems (ES, EN, DE, FR, PL, RU y más)
- Configuración del Discord Bot (token, servidor, canal)

---

## Requisitos

- **Windows 10 / 11** (64-bit)
- [**Npcap**](https://npcap.com/) — necesario para la captura de tráfico de red en tiempo real
- Albion Online instalado (para cargar los datos del juego)

> La captura de red puede requerir ejecutar la app **como administrador** dependiendo de la configuración del antivirus.

---

## Instalación

1. Descarga el ZIP de la [última release](https://github.com/crygeo/Albion-App/releases/latest)
2. Instala [Npcap](https://npcap.com/) si no lo tienes
3. Descomprime el ZIP en cualquier carpeta
4. Ejecuta `MainApp.exe`

No requiere instalar .NET por separado — el runtime va incluido en el ZIP.

---

## Stack técnico

| Capa | Tecnología |
|------|-----------|
| UI | WPF (.NET 9) + MaterialDesignInXamlToolkit 5.3 |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| Persistencia | EF Core + SQLite |
| Red | SharpPcap / Npcap + decodificador Photon UDP |
| Discord | Discord.Net |
| Datos del juego | XMLs cifrados del cliente de Albion |
| Imágenes | CDN `render.albiononline.com` + caché en disco |

Arquitectura Clean (Domain → Application → Infrastructure → Presentation). Composition root manual en `App.xaml.cs`, sin contenedor DI externo.

---

## Actualización

La app comprueba automáticamente si hay una nueva versión al arrancar. Si hay una disponible, aparece una notificación en la parte inferior con un enlace a la release.
