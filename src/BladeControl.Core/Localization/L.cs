using System.Globalization;

namespace BladeControl.Core.Localization;

/// <summary>The languages the interface is written in.</summary>
internal enum AppLanguage
{
    English,
    Spanish
}

/// <summary>
/// Translates the interface. Every on-screen text is written in English in the
/// code and passed through <see cref="T"/> (or <see cref="F"/> when it carries
/// values); in Spanish the English text is looked up in <see cref="Spanish"/>.
/// A text without a translation shows in English rather than not at all.
/// The language is chosen once at startup: changing it restarts the app.
/// </summary>
internal static class L
{
    /// <summary>The language on screen. English until startup picks one, which keeps the tests in English.</summary>
    public static AppLanguage Current { get; set; } = AppLanguage.English;

    /// <summary>The setting's value for each language, as stored in settings.json.</summary>
    public static string Code(AppLanguage language) => language == AppLanguage.Spanish ? "es" : "en";

    /// <summary>
    /// The language for a stored setting: "es" or "en", or, with none stored,
    /// the Windows display language (Spanish for any Spanish, English otherwise).
    /// </summary>
    public static AppLanguage Resolve(string? code, CultureInfo windowsCulture) => code?.ToLowerInvariant() switch
    {
        "es" => AppLanguage.Spanish,
        "en" => AppLanguage.English,
        _ => windowsCulture.TwoLetterISOLanguageName == "es" ? AppLanguage.Spanish : AppLanguage.English
    };

    /// <summary>The text in the current language.</summary>
    public static string T(string english) =>
        Current == AppLanguage.Spanish && Spanish.TryGetValue(english, out var spanish) ? spanish : english;

    /// <summary>A text with values: <paramref name="englishFormat"/> uses {0}, {1}... as string.Format does.</summary>
    public static string F(string englishFormat, params object?[] values) =>
        string.Format(CultureInfo.InvariantCulture, T(englishFormat), values);

    internal static readonly IReadOnlyDictionary<string, string> Spanish = new Dictionary<string, string>
    {
        // Tray icon and window titles.
        ["Free up GPU"] = "Liberar GPU",
        ["Settings"] = "Ajustes",
        ["Open Blade Control"] = "Abrir Blade Control",
        ["Exit"] = "Salir",
        ["Close"] = "Cerrar",
        ["Loading"] = "Cargando",
        ["Done"] = "Hecho",
        ["Failed"] = "Falló",
        ["Close (Blade Control keeps running in the tray)"] = "Cerrar (Blade Control sigue en la bandeja)",

        // The sidebar's sections and their pages.
        ["Performance"] = "Rendimiento",
        ["Power profiles"] = "Perfiles de energía",
        ["Refresh rate"] = "Frecuencia de actualización",
        ["Device"] = "Equipo",
        ["Assistant"] = "Asistente",
        ["Collapse menu"] = "Contraer menú",
        ["Expand menu"] = "Expandir menú",
        ["System"] = "Sistema",
        ["Power source profiles"] = "Perfiles por fuente de energía",
        ["Power plan"] = "Plan de energía",
        ["The one Windows uses."] = "El que usa Windows.",
        ["Install Razer Blade plan"] = "Instalar plan Razer Blade",
        ["Installing..."] = "Instalando...",
        ["Razer Blade plan installed and in use."] = "Plan Razer Blade instalado y en uso.",
        ["Could not install the Razer Blade plan."] = "No se pudo instalar el plan Razer Blade.",
        ["Windows did not change the plan."] = "Windows no cambió el plan.",
        ["When you plug in or unplug the charger, switches to the profile chosen for each."] = "Al conectar o desconectar el cargador, cambia al perfil elegido para cada caso.",

        // Optimize.
        ["Optimize"] = "Optimizar",
        ["Free up"] = "Liberar",
        ["Clean"] = "Limpiar",
        ["Free up memory"] = "Liberar memoria",
        ["Frees unused RAM and clears Windows' cache (asks for permission)."] = "Libera la RAM sin usar y vacía la caché de Windows (pide permiso).",
        ["Temporary files"] = "Archivos temporales",
        ["Scanning..."] = "Analizando...",
        ["Closes the apps keeping the dedicated GPU awake, if you confirm."] = "Cierra las apps que mantienen activa la GPU dedicada, si lo confirmas.",
        ["Freeing up memory..."] = "Liberando memoria...",
        ["{0} freed from {1} programs in the background."] = "{0} liberados de {1} programas en segundo plano.",
        ["Could not free up memory."] = "No se pudo liberar la memoria.",
        ["The memory cache was kept: administrator permission was not given."] = "La caché de memoria se mantuvo: no se dio permiso de administrador.",
        ["The memory cache could not be cleared."] = "No se pudo vaciar la caché de memoria.",
        ["{0} of memory cache cleared."] = "{0} de caché de memoria vaciados.",
        ["Nothing to clean: no temporary files over a day old."] = "Nada que limpiar: no hay archivos temporales de más de un día.",
        ["{0} in {1} files over a day old."] = "{0} en {1} archivos de más de un día.",
        ["Could not read the temporary folder."] = "No se pudo leer la carpeta temporal.",
        ["Cleaning..."] = "Limpiando...",
        ["{0} freed ({1} files)."] = "{0} liberados ({1} archivos).",
        ["{0} in use were left."] = "{0} en uso se quedaron.",
        ["Could not clean the temporary files."] = "No se pudieron limpiar los archivos temporales.",
        ["Nothing to clean right now."] = "Nada que limpiar por ahora.",
        ["{0} to clean: temporary files, Recycle Bin and Windows Update."] = "{0} para limpiar: archivos temporales, papelera y Windows Update.",
        ["Empty the Recycle Bin too? It holds {0} in {1} items, which cannot be recovered afterwards."] = "¿Vaciar también la papelera? Tiene {0} en {1} elementos, que no se podrán recuperar después.",
        ["{0} freed."] = "{0} liberados.",
        ["Windows' caches were kept: administrator permission was not given."] = "Las cachés de Windows se mantuvieron: no se dio permiso de administrador.",
        ["Windows' caches could not all be cleared."] = "No se pudieron vaciar todas las cachés de Windows.",
        ["NVIDIA shader cache"] = "Caché de sombreadores de NVIDIA",
        ["Clear"] = "Vaciar",
        ["Clearing..."] = "Vaciando...",
        ["Empty. Games rebuild it as they load."] = "Vacía. Los juegos la rehacen al cargar.",
        ["{0} in {1} files. Clearing it can fix stutter after a driver update."] = "{0} en {1} archivos. Vaciarla puede quitar tirones tras actualizar el driver.",
        ["Could not read the shader cache."] = "No se pudo leer la caché de sombreadores.",
        ["Could not clear the shader cache."] = "No se pudo vaciar la caché de sombreadores.",
        ["No NVIDIA shader cache on this PC."] = "No hay caché de sombreadores de NVIDIA en este equipo.",
        ["Hibernation"] = "Hibernación",
        ["Off, with no file on disk. Hibernate and fast startup are off too."] = "Desactivada, sin archivo en el disco. La hibernación y el inicio rápido también.",
        ["Its file takes {0}. Off frees it, with no hibernate or fast startup."] = "Su archivo ocupa {0}. Desactivarla lo libera, sin hibernar ni inicio rápido.",
        ["Off frees its file's disk space, but turns off hibernate and fast startup."] = "Desactivarla libera el espacio de su archivo, pero quita la hibernación y el inicio rápido.",
        ["Asking Windows (asks for permission)..."] = "Pidiéndolo a Windows (pide permiso)...",
        ["Kept as it was: administrator permission was not given."] = "Se dejó como estaba: no se dio permiso de administrador.",
        ["Windows did not make the change."] = "Windows no hizo el cambio.",

        // Status messages.
        ["{0} shortcut is used by another program."] = "Otro programa ya usa el atajo {0}.",
        ["{0} control interface found."] = "Control de {0} encontrado.",
        ["{0} control interface found (community-reported product id, not verified on this model)."] =
            "Control de {0} encontrado (identificador aportado por la comunidad, sin verificar en este modelo).",
        ["No supported Razer laptop detected; fan and battery controls unavailable."] =
            "No se detectó un portátil Razer compatible; los controles de ventilador y batería no están disponibles.",

        // Settings window.
        ["Language"] = "Idioma",
        ["Blade Control restarts to change the language."] = "Blade Control se reinicia para cambiar el idioma.",
        ["Start at login"] = "Iniciar con Windows",
        ["Opens in the tray when you sign in."] = "Se abre en la bandeja al iniciar sesión.",
        ["Hide when clicking away"] = "Ocultar al hacer clic fuera",
        ["Off, it stays open until you click the tray icon."] = "Si no, sigue abierta hasta pulsar el icono de la bandeja.",
        ["Always on top"] = "Siempre visible",
        ["Stays above other windows and games. {0} shows or hides it."] = "Queda sobre otras ventanas y juegos. {0} la muestra u oculta.",
        ["Free up GPU when unplugged"] = "Liberar GPU sin cargador",
        ["Offers to close apps using the dedicated GPU, to save battery."] = "Ofrece cerrar las apps que usan la GPU dedicada, para ahorrar batería.",
        ["Performance mode shortcuts"] = "Atajos de los modos de rendimiento",
        ["Performance mode"] = "Modo de rendimiento",
        ["Ctrl+Shift+F1, F2 and F3: Silent, Balanced and Custom."] = "Ctrl+Shift+F1, F2 y F3: Silencio, Equilibrado y Personalizado.",
        ["Turn off lighting when display is off"] = "Apagar la iluminación con la pantalla apagada",
        ["Turn off lighting when idle for:"] = "Apagar la iluminación tras inactividad de:",
        ["Turn off lighting when battery is below:"] = "Apagar la iluminación con batería por debajo de:",
        ["{0} min"] = "{0} min",

        // Settings: maintenance.
        ["Maintenance"] = "Mantenimiento",
        ["Razer drivers and support, logs, reset."] = "Drivers y soporte de Razer, registros, restablecer.",
        ["Drivers"] = "Drivers",
        ["Logs"] = "Registros",
        ["Reset"] = "Restablecer",
        ["Reset to defaults"] = "Restablecer valores",
        ["Your settings"] = "Tus ajustes",
        ["Save your modes, app profiles and options to a file, or bring them back from one."] = "Guarda tus modos, perfiles de apps y opciones en un archivo, o recupéralos desde uno.",
        ["Export"] = "Exportar",
        ["Import"] = "Importar",
        ["Export settings"] = "Exportar ajustes",
        ["Import settings"] = "Importar ajustes",
        ["Settings exported."] = "Ajustes exportados.",
        ["Could not export the settings."] = "No se pudieron exportar los ajustes.",
        ["The file could not be read."] = "No se pudo leer el archivo.",
        ["This file is not a Blade Control settings file."] = "Este archivo no es un archivo de ajustes de Blade Control.",
        ["This file was made by a newer Blade Control. Update Blade Control and try again."] = "Este archivo lo creó una versión más nueva de Blade Control. Actualiza Blade Control e inténtalo de nuevo.",
        ["Replace your settings with the ones in this file?\r\n\r\nIt brings your performance modes, {0}, and the display, lighting and Blade Control options."] =
            "¿Reemplazar tus ajustes por los de este archivo?\r\n\r\nTrae tus modos de rendimiento, {0} y las opciones de pantalla, iluminación y Blade Control.",
        ["1 app profile"] = "1 perfil de app",
        ["{0} app profiles"] = "{0} perfiles de apps",
        ["Apps not on this PC: {0}. Their profiles are kept for when you install them."] = "Apps que no están en este PC: {0}. Sus perfiles se guardan para cuando las instales.",
        ["Blade Control will restart."] = "Blade Control se reiniciará.",
        ["Could not change Start at login."] = "No se pudo cambiar Iniciar con Windows.",
        ["Reset Blade Control to how it was the first time you opened it?\r\n\r\nThis will:\r\n  - clear your saved settings, including the options in this window and your never-close list\r\n  - turn off Start at login\r\n  - set the laptop to Balanced mode with no battery charge limit\r\n\r\nIt will not change Razer's background services or anything else on your PC. Blade Control will restart."] =
            "¿Dejar Blade Control como estaba la primera vez que lo abriste?\r\n\r\nEsto:\r\n  - borra tus ajustes guardados, incluidas las opciones de esta ventana y tu lista de apps que nunca se cierran\r\n  - desactiva Iniciar con Windows\r\n  - pone el portátil en modo Equilibrado sin límite de carga\r\n\r\nNo cambia los servicios de Razer ni nada más de tu PC. Blade Control se reiniciará.",
        ["Most of the reset worked, but not everything:"] = "Casi todo se restableció, pero no todo:",
        ["Blade Control will restart now. Details are in the log."] = "Blade Control se reiniciará ahora. Los detalles están en el registro.",
        ["The reset is done, but Blade Control could not restart itself. Please close it from the tray icon and open it again."] =
            "Se restableció, pero Blade Control no pudo reiniciarse solo. Ciérralo desde el icono de la bandeja y vuelve a abrirlo.",
        ["Blade Control could not restart itself. Please close it from the tray icon and open it again."] =
            "Blade Control no pudo reiniciarse solo. Ciérralo desde el icono de la bandeja y vuelve a abrirlo.",
        ["Your saved settings could not be cleared."] = "No se pudieron borrar tus ajustes guardados.",
        ["Start at login could not be turned off."] = "No se pudo desactivar Iniciar con Windows.",
        ["The laptop could not be set to Balanced mode."] = "No se pudo poner el portátil en modo Equilibrado.",
        ["The battery charge limit could not be removed."] = "No se pudo quitar el límite de carga.",

        // Free up GPU.
        ["Save battery?"] = "¿Ahorrar batería?",
        ["Ask them to close"] = "Pedir que se cierren",
        ["Not now"] = "Ahora no",
        ["These apps are keeping the dedicated GPU awake, which drains the battery:"] =
            "Estas apps mantienen despierta la GPU dedicada y gastan batería:",
        ["Ask them to close? Each one can still ask you to save your work first."] =
            "¿Pedirles que se cierren? Cada una puede pedirte antes que guardes tu trabajo.",
        ["{0} instances, "] = "{0} instancias, ",
        ["No dedicated GPU was found, so there is nothing to free up."] = "No se encontró GPU dedicada, así que no hay nada que liberar.",
        ["An external display is connected (or Windows could not say). It is driven by the dedicated GPU, so the GPU stays on whatever is closed. Disconnect it and try again."] =
            "Hay una pantalla externa conectada (o Windows no pudo confirmarlo). La mueve la GPU dedicada, así que la GPU sigue encendida aunque se cierre todo. Desconéctala y vuelve a intentarlo.",
        ["No apps that can be closed are using the dedicated GPU. Windows, drivers, terminals, editors and background helpers are left alone."] =
            "Ninguna app que se pueda cerrar está usando la GPU dedicada. Windows, drivers, terminales, editores y procesos de fondo no se tocan.",
        ["Things changed while you were deciding, so nothing was closed. Try again."] =
            "Algo cambió mientras decidías, así que no se cerró nada. Vuelve a intentarlo.",
        ["Already checking the dedicated GPU."] = "Ya se está revisando la GPU dedicada.",
        ["Could not check the dedicated GPU. Details are in the log."] = "No se pudo revisar la GPU dedicada. Los detalles están en el registro.",

        // Performance.
        ["System monitor"] = "Monitor del sistema",
        ["Balanced"] = "Equilibrado",
        ["Gaming"] = "Juego",
        ["Custom"] = "Personalizado",
        ["Speed"] = "Velocidad",
        ["Temperature"] = "Temperatura",
        ["Frequency"] = "Frecuencia",
        ["Available"] = "Disponible",
        ["Asleep"] = "En reposo",
        ["Silent"] = "Silencio",
        ["Low"] = "Bajo",
        ["Medium"] = "Medio",
        ["High"] = "Alto",
        ["Boost"] = "Turbo",
        ["Needs to be plugged in"] = "Requiere el cargador conectado",
        ["Not supported on this laptop"] = "No compatible con este portátil",
        ["This laptop does not support that mode."] = "Este portátil no admite ese modo.",
        ["Active"] = "Activo",
        ["Busy, try again in a moment"] = "Ocupado, inténtalo de nuevo en un momento",
        ["Could not change max fan speed."] = "No se pudo cambiar la velocidad máxima del ventilador.",
        ["Could not apply the power profile."] = "No se pudo aplicar el perfil de energía.",
        ["Could not change the performance mode."] = "No se pudo cambiar el modo de rendimiento.",
        ["Could not change the boost level."] = "No se pudo cambiar el nivel de turbo.",
        ["Performance profile applied."] = "Perfil de rendimiento aplicado.",

        // Fans.
        ["Max"] = "Máx.",
        ["Max RPM"] = "RPM máximas",
        ["Now {0} · {1} RPM"] = "Ahora {0} · {1} RPM",
        ["Always runs at 100%, however it is used"] = "Siempre al 100 %, sin importar el uso",
        ["Needs Custom mode, plugged in"] = "Requiere modo Personalizado y el cargador conectado",
        ["Needs to be plugged in, and not in Silent mode"] = "Requiere el cargador conectado y no estar en modo Silencio",
        ["This laptop does not support max fan speed"] = "Este portátil no permite la velocidad máxima del ventilador",

        // Display.
        ["Display"] = "Pantalla",
        ["Size"] = "Tamaño",
        ["Graphics"] = "Gráficos",
        ["More information"] = "Más información",
        ["Less information"] = "Menos información",
        ["Integrated graphics only"] = "Solo gráficos integrados (iGPU)",
        ["This laptop has no dedicated GPU to turn off."] = "Este portátil no tiene una GPU dedicada que apagar.",
        ["{0} is on: Windows uses it when an app needs it."] = "{0} está encendida: Windows la usa cuando una app la necesita.",
        ["{0} is off: everything runs on the integrated GPU."] = "{0} está apagada: todo funciona con la GPU integrada.",
        ["Disconnect the external display first: its port runs on the dedicated GPU."] =
            "Desconecta antes la pantalla externa: su puerto funciona con la GPU dedicada.",
        ["Turn off the dedicated GPU?\r\n\r\nEverything will run on the integrated GPU, which saves battery, but:\r\n  - the HDMI and USB-C video ports will not show a picture\r\n  - games and 3D apps will run much slower\r\n  - apps using the dedicated GPU will be asked to close first\r\n\r\nIt stays off, even after restarting, until you turn this switch off. Windows will ask for administrator permission."] =
            "¿Apagar la GPU dedicada?\r\n\r\nTodo funcionará con la GPU integrada, que ahorra batería, pero:\r\n  - los puertos de vídeo HDMI y USB-C no darán imagen\r\n  - los juegos y las apps 3D irán mucho más lentos\r\n  - antes se pedirá cerrar las apps que usan la GPU dedicada\r\n\r\nSeguirá apagada, incluso tras reiniciar, hasta que desactives este interruptor. Windows pedirá permiso de administrador.",
        ["Closing the apps on the dedicated GPU..."] = "Cerrando las apps de la GPU dedicada...",
        ["Kept on: the apps using it were not closed."] = "Sigue encendida: no se cerraron las apps que la usan.",
        ["Kept on: the apps using it could not be checked."] = "Sigue encendida: no se pudieron revisar las apps que la usan.",
        ["The dedicated GPU is on again."] = "La GPU dedicada vuelve a estar encendida.",
        ["The dedicated GPU is off."] = "La GPU dedicada está apagada.",
        ["Restart Windows to finish the change."] = "Reinicia Windows para terminar el cambio.",
        ["On, the dedicated GPU is turned off and everything runs on the integrated one: more battery and less heat. Off, Windows picks the GPU by itself, as usual."] =
            "Activado, la GPU dedicada se apaga y todo funciona con la integrada: más batería y menos calor. Desactivado, Windows elige la GPU solo, como siempre.",
        ["While it is on:"] = "Mientras esté activado:",
        ["- The HDMI and USB-C video ports show no picture: they run on the dedicated GPU."] =
            "- Los puertos de vídeo HDMI y USB-C no dan imagen: funcionan con la GPU dedicada.",
        ["- Games and 3D apps run much slower."] = "- Los juegos y las apps 3D van mucho más lentos.",
        ["- It stays on after restarting, until you turn it off. A driver update may turn the GPU back on."] =
            "- Sigue activado tras reiniciar, hasta que lo desactives. Una actualización del driver puede volver a encender la GPU.",
        ["The dedicated GPU is off, so this display gets no picture. Turn Integrated graphics only off to use it."] =
            "La GPU dedicada está apagada, así que esta pantalla no recibe imagen. Desactiva Solo gráficos integrados para usarla.",
        ["Color profile"] = "Perfil de color",
        ["Standard"] = "Estándar",
        ["Night light"] = "Luz nocturna",
        ["Neutral"] = "Neutro",
        ["Contrast"] = "Contraste",
        ["This screen does not take it"] = "Esta pantalla no lo admite",
        ["Less blue, easier on the eyes at night."] = "Menos azul, más descanso para la vista de noche.",
        ["A more balanced white, recommended for photo editing."] = "Un blanco más balanceado, recomendado para edición fotográfica.",
        ["Deeper darks and brighter lights."] = "Oscuros más profundos y claros más brillantes.",
        ["The colors as the panel shows them."] = "Los colores tal como los muestra el panel.",
        ["External display"] = "Pantalla externa",
        ["External display detected"] = "Pantalla externa detectada",
        ["Keep running with the lid closed"] = "Seguir funcionando con la tapa cerrada",
        ["With a monitor connected and the charger plugged in, closing the lid does not put the laptop to sleep. On battery it still does."] =
            "Con un monitor conectado y el cargador enchufado, cerrar la tapa no suspende el portátil. Con batería sí lo hace.",
        ["No external display"] = "Sin pantalla externa",
        ["To choose the refresh rate on a secondary monitor connected by HDMI, use the Windows "] = "Para elegir la frecuencia de un monitor secundario conectado por HDMI, usa la opción de Windows ",
        ["Display Settings"] = "Configuración de pantalla",
        ["Auto: power source unavailable."] = "Auto: no se sabe si está enchufado.",
        ["Auto: waiting for the game"] = "Auto: esperando al juego",
        ["{0} Hz is not available for the current display mode."] = "{0} Hz no está disponible en el modo de pantalla actual.",
        ["Windows could not apply {0} Hz. Error: {1}."] = "Windows no pudo aplicar {0} Hz. Error: {1}.",
        ["Switched to {0} Hz."] = "Cambiado a {0} Hz.",
        ["{0} Hz is not available."] = "{0} Hz no está disponible.",
        ["{0} Hz is not available at {1}x{2}."] = "{0} Hz no está disponible a {1}x{2}.",

        // Lighting.
        ["Lighting"] = "Iluminación",
        ["Keyboard"] = "Teclado",
        ["Game mode"] = "Modo juego",
        ["Game mode: {0}"] = "Modo juego: {0}",
        ["Windows key"] = "Tecla Windows",
        ["Windows key and Alt+Tab"] = "Tecla Windows y Alt+Tab",
        ["Windows key, Alt+Tab and Alt+F4"] = "Tecla Windows, Alt+Tab y Alt+F4",
        ["Logo"] = "Logo",
        ["Off"] = "Apagado",
        ["On"] = "Encendido",
        ["Static"] = "Fijo",
        ["Static green"] = "Verde fijo",
        ["Color"] = "Color",
        ["{0} brightness"] = "Brillo de {0}",
        ["Spectrum"] = "Espectro",
        ["Breathing"] = "Respiración",
        ["Wave"] = "Onda",
        ["White"] = "Blanco",
        ["Razer green"] = "Verde Razer",
        ["Red"] = "Rojo",
        ["Orange"] = "Naranja",
        ["Yellow"] = "Amarillo",
        ["Cyan"] = "Cian",
        ["Blue"] = "Azul",
        ["Purple"] = "Morado",
        ["Pink"] = "Rosa",
        ["Could not change the keyboard lighting."] = "No se pudo cambiar la luz del teclado.",
        ["Could not change the keyboard brightness."] = "No se pudo cambiar el brillo del teclado.",
        ["Could not change the logo lighting."] = "No se pudo cambiar la luz del logo.",
        ["Could not change the logo brightness."] = "No se pudo cambiar el brillo del logo.",
        ["Could not change the keyboard color."] = "No se pudo cambiar el color del teclado.",
        ["Could not read the lighting state."] = "No se pudo leer el estado de la iluminación.",
        ["Could not read the lighting state after a change."] = "No se pudo leer la iluminación después del cambio.",

        // Battery.
        ["Charge limit"] = "Límite de carga",
        ["Limit:"] = "Límite:",
        ["Plugged in"] = "Enchufado",
        ["On battery"] = "Con batería",
        ["This laptop does not support a battery charge limit"] = "Este portátil no permite limitar la carga",
        ["Could not restore the saved battery charge limit ({0}%)."] = "No se pudo recuperar el límite de carga guardado ({0} %).",
        ["Battery charge limit disabled. Charging is allowed to 100%."] = "Límite de carga desactivado. Carga hasta el 100 %.",
        ["Battery charge limit set to {0}%."] = "Límite de carga fijado en {0} %.",
        ["Battery charge-limit change to {0}% failed."] = "Falló el cambio del límite de carga a {0} %.",
        ["Could not change the battery charge limit."] = "No se pudo cambiar el límite de carga.",

        // Battery window.
        ["Battery"] = "Batería",
        ["Unavailable"] = "No disponible",
        ["No information"] = "Sin información",
        ["Windows did not report the battery"] = "Windows no informó de la batería",
        ["Windows did not report these figures"] = "Windows no informó de estos datos",
        ["Power"] = "Potencia",
        ["Time"] = "Tiempo",
        ["Charge"] = "Carga",
        ["Health"] = "Salud",
        ["Voltage"] = "Voltaje",
        ["Cycles"] = "Ciclos",
        ["{0} cycles"] = "{0} ciclos",

        // System information window.
        ["System information"] = "Información del equipo",
        ["Reading..."] = "Leyendo...",
        ["Model"] = "Modelo",
        ["Razer controls connected"] = "Controles de Razer conectados",
        ["Razer controls not detected"] = "Controles de Razer no detectados",
        ["Operating system"] = "Sistema operativo",
        ["Integrated GPU"] = "GPU integrada",
        ["{0} of {1}"] = "{0} de {1}",
        ["{0} free"] = "{0} libres",
        ["build {0}"] = "compilación {0}",
        ["{0} cores · {1} threads"] = "{0} núcleos · {1} hilos",
        ["{0} threads"] = "{0} hilos",

        ["Charging"] = "Cargando",
        ["Using"] = "Consumiendo",
        ["left"] = "restante",
        ["to full"] = "para cargar",
        ["of {0}"] = "de {0}",
        ["{0} of {1} (factory)"] = "{0} de {1} (fábrica)",
        ["Plugged in, not charging"] = "Enchufado, sin cargar",
        ["Lithium-ion"] = "Ion de litio",
        ["Lithium polymer"] = "Polímero de litio",
        ["Nickel-metal hydride"] = "Níquel-metalhidruro",
        ["Nickel-cadmium"] = "Níquel-cadmio",
        ["Lead-acid"] = "Plomo-ácido",

        // Razer software.
        ["Razer Software Running: --"] = "Software Razer activo: --",
        ["Razer Software Running: {0}"] = "Software Razer activo: {0}",
        ["Razer Software Running: 0 (starts at login)"] = "Software Razer activo: 0 (arranca con Windows)",
        ["Razer Software Running: 0"] = "Software Razer activo: 0",
        ["Stop"] = "Detener",
        ["Start"] = "Iniciar",
        ["Tip: uninstall Razer Synapse for the cleanest experience."] = "Consejo: desinstala Razer Synapse para una experiencia más limpia.",
        ["Could not change the Razer software."] = "No se pudo cambiar el software de Razer.",
        ["Stop Razer software"] = "Detener software de Razer",
        ["Stopping Razer software..."] = "Deteniendo software de Razer...",
        ["Starting Razer software..."] = "Iniciando software de Razer...",
        ["Administrator approval was declined. Nothing was changed."] = "Se rechazó el permiso de administrador. No se cambió nada.",
        ["Some Razer services could not be stopped."] = "Algunos servicios de Razer no se pudieron detener.",
        ["Razer's startup at login could not be turned off."] = "No se pudo desactivar el arranque de Razer con Windows.",
        ["{0} See the log for details."] = "{0} Mira el registro para más detalles.",
        ["Razer software stopped and kept off."] = "Software de Razer detenido y desactivado.",
        ["Some Razer services could not be restored."] = "Algunos servicios de Razer no se pudieron restaurar.",
        ["Razer's startup at login could not be restored."] = "No se pudo restaurar el arranque de Razer con Windows.",
        ["Razer software restored."] = "Software de Razer restaurado.",
        ["Stop Razer's background software?"] = "¿Detener el software de fondo de Razer?",
        ["These {0} services will be stopped and kept off, including after a restart:"] =
            "Estos {0} servicios se detendrán y seguirán apagados, también tras reiniciar:",
        ["These Razer programs are running and will be asked to close (nothing is force-closed):"] =
            "Estos programas de Razer están abiertos y se les pedirá que se cierren (nada se cierra a la fuerza):",
        ["Razer will be stopped from starting when you sign in, the same as switching it off in Task Manager's Startup tab:"] =
            "Razer dejará de arrancar al iniciar sesión, igual que al desactivarlo en la pestaña Inicio del Administrador de tareas:",
        ["Razer devices connected now: {0}."] = "Dispositivos Razer conectados ahora: {0}.",
        ["No other Razer devices are connected right now."] = "No hay otros dispositivos Razer conectados ahora.",
        ["While the services are off, Razer-only features can't be configured on those devices (button remapping, macros, lighting effects, DPI stages). The devices still work as normal."] =
            "Con los servicios apagados, las funciones exclusivas de Razer no se pueden configurar en esos dispositivos (reasignar botones, macros, efectos de luz, niveles de DPI). Los dispositivos siguen funcionando con normalidad.",
        ["Press Start to bring everything back exactly as it was."] = "Pulsa Iniciar para dejarlo todo exactamente como estaba.",
        ["Windows will ask for administrator approval."] = "Windows pedirá permiso de administrador.",
        ["{0} running = {1} services + {2} Razer programs"] = "{0} activos = {1} servicios + {2} programas de Razer",
        ["Services: {0} of {1} running"] = "Servicios: {0} de {1} activos",
        ["  ({0} not yet disabled)"] = "  ({0} aún sin desactivar)",
        ["Programs: none running"] = "Programas: ninguno abierto",
        ["Programs running:"] = "Programas abiertos:",
        ["Checked at {0}"] = "Revisado a las {0}",
        ["Start at login: no Razer entry found"] = "Arranque con Windows: no hay entrada de Razer",
        ["Start at login: ON ({0})"] = "Arranque con Windows: SÍ ({0})",
        ["Start at login: off"] = "Arranque con Windows: no",

        // Errors.
        ["Blade Control hit an unexpected error but is still running.\n\nIf something stops working, restart it. Details were saved to:\n"] =
            "Blade Control tuvo un error inesperado pero sigue funcionando.\n\nSi algo deja de funcionar, reinícialo. Los detalles se guardaron en:\n",
        ["Blade Control hit an unexpected error and has to close.\n\nDetails were saved to:\n"] =
            "Blade Control tuvo un error inesperado y tiene que cerrarse.\n\nLos detalles se guardaron en:\n",

        // Blade Control's window: title bar and page row.
        ["Blade Control"] = "Blade Control",
        ["Minimize"] = "Minimizar",
        ["Maximize"] = "Maximizar",
        ["Restore"] = "Restaurar",
        ["Read the laptop again"] = "Volver a leer el portátil",
        ["This page is coming soon."] = "Esta página llegará pronto.",

        // Performance page.
        ["Power mode"] = "Modo de energía",
        ["Plugged In"] = "Enchufado",
        ["On Battery"] = "Con batería",
        ["Custom is available when plugged in."] = "Personalizado está disponible con el cargador enchufado.",
        ["CPU preset"] = "Nivel de CPU",
        ["GPU preset"] = "Nivel de GPU",
        ["Run maximum fan speed"] = "Ventiladores al máximo",
        ["Both fans at full speed for better cooling. Plugged in only, in any mode but Silent."] =
            "Los dos ventiladores a toda velocidad para enfriar mejor. Solo enchufado, en cualquier modo menos Silencio.",
        ["Both fans at full speed for better cooling. Plugged in only, in Custom mode."] =
            "Los dos ventiladores a toda velocidad para enfriar mejor. Solo enchufado, en modo Personalizado.",
        ["Switch profile with the charger"] = "Cambiar de perfil con el cargador",
        ["Plugging in or unplugging applies that power source's mode."] = "Al enchufar o desenchufar se aplica el modo de esa fuente de energía.",
        ["Could not read the performance mode."] = "No se pudo leer el modo de rendimiento.",
        ["Saved. It is used when the charger is plugged in."] = "Guardado. Se usa al enchufar el cargador.",
        ["Saved. It is used when the laptop runs on battery."] = "Guardado. Se usa cuando el portátil va con batería.",

        // Battery health.
        ["Battery health"] = "Salud de la batería",
        ["Optimize battery charging"] = "Optimizar la carga",
        ["Increase your battery's longevity by limiting its maximum charge."] = "Alarga la vida de la batería limitando su carga máxima.",
        ["Keeping your battery charged below 100% puts it under less stress, which helps reduce battery aging."] =
            "Mantener la batería por debajo del 100% la somete a menos estrés, lo que ayuda a que envejezca más despacio.",
        ["This laptop's controller does not accept a charge limit. A BIOS update from Razer may add it."] =
            "El controlador de este portátil no acepta un límite de carga. Una actualización de BIOS de Razer podría añadirlo.",
        ["The battery charges to 100% again."] = "La batería vuelve a cargar hasta el 100%.",
        ["The battery now stops charging at {0}%."] = "La batería ahora deja de cargar al {0}%.",
        ["Could not set the battery charge limit."] = "No se pudo poner el límite de carga.",

        // Device panel.
        ["Not detected"] = "No detectado",
        ["Plugged in, fully charged"] = "Enchufado, carga completa",
        ["Temperatures"] = "Temperaturas",
        ["Last 1 h avg."] = "Prom. 1 h",
        ["Fans"] = "Ventiladores",

        // Lighting page.
        ["System lighting"] = "Iluminación del sistema",
        ["Keyboard brightness"] = "Brillo del teclado",
        ["Lighting effects"] = "Efectos de iluminación",
        ["Keyboard effect"] = "Efecto del teclado",
        ["Logo effect"] = "Efecto del logo",
        ["A steady light in the color you choose."] = "Una luz fija del color que elijas.",
        ["A steady light in Razer green."] = "Una luz fija en verde Razer.",
        ["The light fades in and out slowly."] = "La luz se enciende y se apaga despacio.",
        ["Cycles smoothly through every color."] = "Recorre todos los colores suavemente.",
        ["Animates all colors in a wave across the keyboard."] = "Todos los colores recorren el teclado en una onda.",
        ["The keyboard backlight is off."] = "La luz del teclado está apagada.",

        // Optimize page.
        ["Memory and storage"] = "Memoria y almacenamiento",
        ["Razer software"] = "Software de Razer",

        // Settings page.
        ["Experimental"] = "Experimental",
        ["Turbo boost off in Silent"] = "Turbo desactivado en Silencio",
        ["Silent keeps the CPU at its base speed, which is what makes it quiet. Off, Silent leaves the turbo as the power plan has it: faster, but warmer and louder."] =
            "Silencio deja la CPU a su velocidad base, que es lo que la hace silenciosa. Desactivado, Silencio deja el turbo como lo tiene el plan de energía: más rápida, pero más caliente y ruidosa.",
        ["Buy me a coffee"] = "Invítame un café",
        ["Blade Control is free and made in my spare time. If it is useful to you, you can buy me a coffee: it helps me keep improving it."] =
            "Blade Control es gratis y lo hago en mi tiempo libre. Si te resulta útil, puedes invitarme un café: me ayuda a seguir mejorándolo.",
        ["Buy me a coffee on PayPal"] = "Invítame un café en PayPal",
        ["General"] = "General",
        ["Opens Blade Control when you sign in to Windows."] = "Abre Blade Control al iniciar sesión en Windows.",
        ["Keeps the window above other windows and games."] = "Mantiene la ventana por encima de otras ventanas y juegos.",
        ["Razer drivers and support"] = "Controladores y soporte de Razer",
        ["BIOS and driver updates for your laptop, on Razer's site."] = "Actualizaciones de BIOS y controladores para tu portátil, en la web de Razer.",
        ["Open"] = "Abrir",
        ["What Blade Control did and any errors, to share if something goes wrong."] = "Lo que hizo Blade Control y sus errores, para compartir si algo falla.",
        ["Clears your settings and puts the laptop back in Balanced. Asks first."] = "Borra tus ajustes y vuelve a poner el portátil en Equilibrado. Pregunta antes.",
        ["Blade Control could not restart itself. Please close it and open it again."] = "Blade Control no pudo reiniciarse solo. Ciérralo y vuelve a abrirlo.",

        // Display page.
        ["Auto"] = "Auto",
        ["Auto uses {0} Hz plugged in and {1} Hz on battery, and waits while a fullscreen game has the screen."] =
            "Auto usa {0} Hz enchufado y {1} Hz con batería, y espera mientras un juego a pantalla completa ocupa la pantalla.",
        ["Resolution: {0}"] = "Resolución: {0}",
        ["Screen colors"] = "Colores de la pantalla",

        // Battery page.
        ["Battery status"] = "Estado de la batería",
        ["Last charge"] = "Última carga",
        ["Last 24 hours"] = "Últimas 24 horas",
        ["Screen on"] = "Pantalla encendida",
        ["Screen off"] = "Pantalla apagada",
        ["Asleep"] = "En suspensión",
        ["Windows did not give its battery record."] = "Windows no entregó su registro de la batería.",
        ["Now"] = "Ahora",
        ["Good condition"] = "En buen estado",
        ["Some wear"] = "Algo desgastada",
        ["Worn"] = "Desgastada",
        ["Not charging"] = "Sin cargar",
        ["Fully charged"] = "Carga completa",
        ["Stored"] = "Almacenada",
        ["Chemistry"] = "Química",
        ["Not reported"] = "No informado",
        ["When new"] = "De fábrica",
        ["Full charge now"] = "Carga completa hoy",
        ["What a full charge holds now, against what it held when new. Batteries lose a little every year."] =
            "Lo que guarda una carga completa hoy frente a cuando era nueva. Las baterías pierden un poco cada año.",
        ["The charge limit is on the Performance page, under Battery health."] =
            "El límite de carga está en la página Rendimiento, en Salud de la batería.",

        // Apps: a profile for each app or game.
        ["Apps"] = "Apps",
        ["App"] = "App",
        ["App profiles"] = "Perfiles de apps",
        ["App profile"] = "Perfil de app",
        ["App profile applied."] = "Perfil de app aplicado.",
        ["Use this profile"] = "Usar este perfil",
        ["Keyboard color"] = "Color del teclado",
        ["Pick an app to choose its settings. While it is running they are used; when it closes, everything goes back as it was."] =
            "Elige una app para ajustar su perfil. Mientras está abierta se usan sus ajustes; al cerrarla, todo vuelve a como estaba.",
        ["Search apps"] = "Buscar apps",
        ["Has its own settings"] = "Tiene ajustes propios",
        ["No app"] = "Ninguna app",
        ["Pick an app in the list to choose what changes while it runs."] = "Elige una app de la lista para decidir qué cambia mientras está abierta.",
        ["No apps found."] = "No se encontraron apps.",
        ["Turn every app profile on or off"] = "Activar o pausar todos los perfiles de apps",
        ["While one of these apps is running, its settings are used. When it closes, everything goes back as it was."] =
            "Mientras una de estas apps está abierta, se usan sus ajustes. Al cerrarla, todo vuelve a como estaba.",
        ["No apps yet. Add a game or an app to give it its own mode, fans, refresh rate, colors and keyboard lighting."] =
            "Aún no hay apps. Añade un juego o una app para darle su propio modo, ventiladores, frecuencia, colores e iluminación del teclado.",
        ["Add app"] = "Añadir app",
        ["Add"] = "Añadir",
        ["Remove app"] = "Quitar app",
        ["Cancel"] = "Cancelar",
        ["Games"] = "Juegos",
        ["Open apps"] = "Apps abiertas",
        ["Find a program..."] = "Buscar un programa...",
        ["Choose the app's program"] = "Elige el programa de la app",
        ["Programs"] = "Programas",
        ["Games installed with Steam, Epic Games, Xbox and Battle.net. Anything that runs from the game's folder counts, its own launcher too."] =
            "Juegos instalados con Steam, Epic Games, Xbox y Battle.net. Cuenta todo lo que se abre desde la carpeta del juego, también su propio launcher.",
        ["No games found in Steam, Epic Games, Xbox or Battle.net."] = "No se encontraron juegos de Steam, Epic Games, Xbox ni Battle.net.",
        ["No other apps with a window are open."] = "No hay otras apps abiertas con ventana.",
        ["Could not list the apps."] = "No se pudieron listar las apps.",
        ["No change"] = "Sin cambios",
        ["No changes yet"] = "Aún sin cambios",
        ["Automatic"] = "Automático",
        ["Maximum"] = "Al máximo",
        ["Max fan"] = "Ventiladores al máximo",
        ["Auto fan"] = "Ventiladores automáticos",
        ["Keyboard lighting"] = "Iluminación del teclado",
        ["Keyboard off"] = "Teclado apagado",
        ["Keyboard: {0}"] = "Teclado: {0}",
        ["Keyboard {0}%"] = "Teclado {0}%",
        ["No change leaves that setting as it is. Custom and maximum fans need the charger: on battery, the battery mode stays."] =
            "Sin cambios deja ese ajuste como está. Personalizado y los ventiladores al máximo necesitan el cargador: con batería se mantiene el modo de batería.",
        ["Settings put back"] = "Ajustes restaurados",
        ["Max fan speed is not available in this mode"] = "Ventiladores al máximo no disponible en este modo",
    };
}
