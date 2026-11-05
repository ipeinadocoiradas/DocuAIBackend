-- =============================================================================
-- Inicialización de la base de datos de DocuAI.
-- Se ejecuta UNA sola vez, cuando el volumen de datos está vacío.
--
-- La migración de EF Core también declara la extensión (HasPostgresExtension),
-- pero crearla aquí como superusuario evita fallos si la cadena de conexión de
-- la aplicación usa en el futuro un rol sin privilegios para CREATE EXTENSION.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS vector;
