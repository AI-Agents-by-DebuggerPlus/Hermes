<?php
if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

if ( class_exists( 'HIR_Activator', false ) ) {
    return;
}

class HIR_Activator {

    public static function activate() {
        self::ensure_database();
    }

    public static function ensure_database() {
        global $wpdb;
        $charset = $wpdb->get_charset_collate();

        require_once ABSPATH . 'wp-admin/includes/upgrade.php';

        // ── Таблица изображений ────────────────────────────────────────────────
        $images_table = $wpdb->prefix . 'hir_images';
        dbDelta( "CREATE TABLE IF NOT EXISTS {$images_table} (
            id          BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT,
            channel     VARCHAR(100)        NOT NULL DEFAULT 'default',
            filename    VARCHAR(255)        NOT NULL,
            mime_type   VARCHAR(100)        NOT NULL DEFAULT 'image/png',
            image_data  LONGBLOB,
            file_path   VARCHAR(500)        DEFAULT NULL,
            meta        TEXT                DEFAULT NULL,
            created_at  DATETIME            NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (id),
            KEY channel (channel),
            KEY created_at (created_at)
        ) {$charset};" );

        // ── Таблица логов ──────────────────────────────────────────────────────
        $logs_table = $wpdb->prefix . 'hir_logs';
        dbDelta( "CREATE TABLE IF NOT EXISTS {$logs_table} (
            id          BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT,
            source      VARCHAR(100)        NOT NULL DEFAULT 'HermesWpf',
            level       VARCHAR(20)         NOT NULL DEFAULT 'info',
            category    VARCHAR(100)        NOT NULL DEFAULT '',
            message     TEXT                NOT NULL,
            exception   TEXT                DEFAULT NULL,
            extra       TEXT                DEFAULT NULL,
            logged_at   DATETIME            NOT NULL,
            created_at  DATETIME            NOT NULL,
            PRIMARY KEY (id),
            KEY level (level),
            KEY source (source),
            KEY logged_at (logged_at)
        ) {$charset};" );

        // ── Опции по умолчанию ─────────────────────────────────────────────────
        $defaults = [
            'hir_ws_port'          => 8765,
            'hir_ws_host'          => '0.0.0.0',
            'hir_max_images'       => 50,
            'hir_save_to_disk'     => 1,
            'hir_upload_subdir'    => 'hermes-images',
            'hir_allowed_channels' => '',
            'hir_secret_token'     => wp_generate_password( 32, false ),
            'hir_max_logs'         => 5000,
            'hir_ws_only'          => 0,
            'hir_ws_client_host'   => '',
            'hir_use_sse'          => 1,
        ];
        foreach ( $defaults as $key => $val ) {
            if ( false === get_option( $key ) ) {
                add_option( $key, $val );
            }
        }

        if ( false === get_option( 'hir_db_version' ) ) {
            add_option( 'hir_db_version', HIR_VERSION );
        } else {
            update_option( 'hir_db_version', HIR_VERSION );
        }
    }

    public static function deactivate() {
        // Данные сохраняем — таблицы не удаляем.
    }
}
