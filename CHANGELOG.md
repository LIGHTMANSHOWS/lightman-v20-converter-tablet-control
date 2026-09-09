# Registro de cambios

## 2026-09-09 · R6 · cliente 16:10, tracking y retorno seguro

- Interfaz pública adaptada a Galaxy Tab S10+ en horizontal 16:10.
- Manifiesto web con orientación horizontal y presentación `fullscreen`; al abrir
  por HTTP en la LAN, el primer gesto solicita pantalla completa y queda un botón
  manual como alternativa.
- Modos reales de Motion Tracking `silhouette` y `particles`, cargados desde el
  catálogo local permitido.
- Endpoints de selección, polling y confirmación por revisión implementados en el
  servidor interno; selección de modos y estado público filtrado disponibles en
  el servidor del cliente.
- El portal del cliente refresca catálogo, experiencia activa y estado de tracking
  cada 1,5 segundos. Un modo sólo aparece **En vivo** después del ACK del tracker.
- Los shows lanzados por V20 regresan automáticamente a Resolume al terminar y el
  botón Stop también fuerza ese retorno. Pause conserva xLights y no conmuta la
  fuente.
- Cada arranque comienza de forma segura en Resolume. Reactivar un mismo modo de
  Tracking genera una revisión nueva y exige una confirmación nueva del tracker.

## 2026-09-09 · autoimportación de shows

- V20 Minimal revisa al iniciar las subcarpetas de `Desktop\shows xlights`.
- Sólo acepta FSEQ con cabecera `PSEQ` y audio asociado dentro de la misma carpeta.
- Añade las playlists nuevas a xSchedule con respaldo, escritura atómica y sin
  alterar playlists manuales.
- Si xSchedule está reproduciendo o pausado, posterga la operación sin tocarlo.
- Los shows detectados aparecen únicamente en el control interno hasta su
  aprobación; el portal del cliente permanece sin cambios.
- El control interno informa shows nuevos, carpetas incompletas y reinicios
  pendientes de xSchedule.

## 2026-09-08 · importación inicial

- Fuente consolidada de LIGHTMAN V20 Minimal.
- Selector exclusivo de Resolume, xLights o Motion Tracking.
- Salidas físicas automáticas mientras V20 permanece abierto.
- Servidor web de tablet en el puerto 8780.
- Control local de xSchedule y catálogo de siete shows.
- Manifiesto portátil V4 con 52 elementos, 15 rutas y 20 223 píxeles visibles.
- Orientaciones vigentes de banderas, techo, ondas, árbol y casa.
- Contrato y ejemplo independiente para el desarrollador de Motion Tracking.
- Segundo servidor web en el puerto 8781 para la experiencia pública del cliente.
- Catálogo de siete experiencias con nombres comerciales y categorías.
- Separación estricta: el cliente no recibe IPs, rutas, playlists ni controles
  técnicos del operador.
- Seis proyectos detectados e incorporados al catálogo comercial: 08–13.
- `Noche de Halloween`, `Enciende la Noche` y `Océano Eléctrico` quedan habilitados;
  `Ciudad de Neón`, `Fuerza Imparable` y `Ritmo en la Ciudad` se muestran como
  `Próximamente` hasta completar sus archivos sincronizados.
- `Fuerza Imparable` y `Ritmo en la Ciudad` quedan desbloqueados exclusivamente
  en el control interno para pruebas, sin publicarlos como disponibles al cliente.
