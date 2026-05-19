<?php
if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

if ( class_exists( 'HIR_Helpers', false ) ) {
    return;
}

class HIR_Helpers {

    public static function verify_token( $token ) {
        $known = (string) get_option( 'hir_secret_token', '' );
        $token = (string) $token;

        if ( $known === '' || $token === '' ) {
            return false;
        }

        if ( function_exists( 'hash_equals' ) ) {
            if ( strlen( $known ) !== strlen( $token ) ) {
                return false;
            }
            return hash_equals( $known, $token );
        }

        return $known === $token;
    }

    public static function table_name() {
        global $wpdb;
        return $wpdb->prefix . 'hir_images';
    }

    public static function table_exists() {
        global $wpdb;
        $table = self::table_name();
        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching
        return $wpdb->get_var( $wpdb->prepare( 'SHOW TABLES LIKE %s', $table ) ) === $table;
    }

    public static function logs_table_name() {
        global $wpdb;
        return $wpdb->prefix . 'hir_logs';
    }

    public static function logs_table_exists() {
        global $wpdb;
        $table = self::logs_table_name();
        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching
        return $wpdb->get_var( $wpdb->prepare( 'SHOW TABLES LIKE %s', $table ) ) === $table;
    }
}
