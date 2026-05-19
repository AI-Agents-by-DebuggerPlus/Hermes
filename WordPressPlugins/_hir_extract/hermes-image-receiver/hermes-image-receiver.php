<?php
/**
 * Plugin Name: Hermes Image Receiver
 * Description: Получает изображения от Hermes.Wpf (REST API) и отображает галерею на сайте.
 * Version:     1.0.6
 * Author:      Hermes
 * License:     GPL-2.0+
 * Text Domain: hermes-image-receiver
 * Requires at least: 5.8
 * Requires PHP: 7.4
 */

if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

if ( defined( 'HIR_VERSION' ) ) {
    return;
}

define( 'HIR_VERSION',     '1.0.6' );
define( 'HIR_PLUGIN_DIR',  plugin_dir_path( __FILE__ ) );
define( 'HIR_PLUGIN_URL',  plugin_dir_url( __FILE__ ) );
define( 'HIR_PLUGIN_FILE', __FILE__ );

/**
 * Загрузка классов (один раз).
 */
function hir_load_plugin_files() {
    static $loaded = false;
    if ( $loaded ) {
        return;
    }
    $loaded = true;

    $files = [
        'includes/class-hir-helpers.php',
        'includes/class-hir-activator.php',
        'includes/class-hir-settings.php',
        'includes/class-hir-rest-api.php',
        'includes/class-hir-shortcode.php',
    ];

    foreach ( $files as $file ) {
        $path = HIR_PLUGIN_DIR . $file;
        if ( ! is_readable( $path ) ) {
            add_action( 'admin_notices', static function () use ( $path ) {
                echo '<div class="notice notice-error"><p><strong>Hermes Receiver:</strong> отсутствует файл '
                    . esc_html( basename( $path ) ) . '</p></div>';
            } );
            return;
        }
        require_once $path;
    }
}

register_activation_hook( HIR_PLUGIN_FILE, static function () {
    hir_load_plugin_files();
    if ( class_exists( 'HIR_Activator', false ) ) {
        HIR_Activator::activate();
    }
} );

register_deactivation_hook( HIR_PLUGIN_FILE, static function () {
    hir_load_plugin_files();
    if ( class_exists( 'HIR_Activator', false ) ) {
        HIR_Activator::deactivate();
    }
} );

if ( ! function_exists( 'hir_init' ) ) {
    function hir_init() {
        hir_load_plugin_files();

        if ( ! class_exists( 'HIR_REST_API', false ) ) {
            return;
        }

        HIR_Settings::init();
        HIR_REST_API::init();
        HIR_Shortcode::init();
    }
    add_action( 'plugins_loaded', 'hir_init', 20 );
}
