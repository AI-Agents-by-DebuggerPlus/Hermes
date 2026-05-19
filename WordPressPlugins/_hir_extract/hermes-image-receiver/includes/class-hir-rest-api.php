<?php
if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

if ( class_exists( 'HIR_REST_API', false ) ) {
    return;
}

class HIR_REST_API {

    public static function init() {
        add_action( 'rest_api_init', [ __CLASS__, 'register_routes' ] );
        add_filter( 'rest_pre_serve_request', [ __CLASS__, 'rest_pre_serve_request' ], 10, 4 );
    }

    /**
     * Позволяет stream() писать raw SSE без обёртки REST-ответа WordPress.
     */
    public static function rest_pre_serve_request( $served, $result, $request, $server ) {
        unset( $result, $server );
        if ( $request instanceof WP_REST_Request && $request->get_route() === '/hermes/v1/stream' ) {
            return true;
        }
        return $served;
    }

    public static function register_routes() {
        register_rest_route( 'hermes/v1', '/message', [
            'methods'             => 'POST',
            'callback'            => [ __CLASS__, 'receive_message' ],
            'permission_callback' => '__return_true',
        ] );

        register_rest_route( 'hermes/v1', '/image', [
            'methods'             => 'POST',
            'callback'            => [ __CLASS__, 'receive_image' ],
            'permission_callback' => '__return_true',
        ] );

        register_rest_route( 'hermes/v1', '/images', [
            'methods'             => 'GET',
            'callback'            => [ __CLASS__, 'get_images' ],
            'permission_callback' => '__return_true',
        ] );

        register_rest_route( 'hermes/v1', '/images', [
            'methods'             => 'DELETE',
            'callback'            => [ __CLASS__, 'delete_images' ],
            'permission_callback' => [ __CLASS__, 'verify_admin' ],
        ] );

        register_rest_route( 'hermes/v1', '/status', [
            'methods'             => 'GET',
            'callback'            => [ __CLASS__, 'get_status' ],
            'permission_callback' => '__return_true',
        ] );

        register_rest_route( 'hermes/v1', '/stream', [
            'methods'             => 'GET',
            'callback'            => [ __CLASS__, 'stream' ],
            'permission_callback' => '__return_true',
        ] );

        register_rest_route( 'hermes/v1', '/image-data/(?P<id>\d+)', [
            'methods'             => 'GET',
            'callback'            => [ __CLASS__, 'serve_image_data' ],
            'permission_callback' => '__return_true',
            'args'                => [
                'id' => [
                    'validate_callback' => function ( $v ) {
                        return is_numeric( $v ) && (int) $v > 0;
                    },
                ],
            ],
        ] );

        register_rest_route( 'hermes/v1', '/logs', [
            'methods'             => 'POST',
            'callback'            => [ __CLASS__, 'receive_logs' ],
            'permission_callback' => '__return_true',
        ] );

        register_rest_route( 'hermes/v1', '/logs', [
            'methods'             => 'GET',
            'callback'            => [ __CLASS__, 'get_logs' ],
            'permission_callback' => [ __CLASS__, 'verify_admin' ],
        ] );

        register_rest_route( 'hermes/v1', '/logs', [
            'methods'             => 'DELETE',
            'callback'            => [ __CLASS__, 'delete_logs' ],
            'permission_callback' => [ __CLASS__, 'verify_admin' ],
        ] );
    }

    // ── Realtime-compatible receive (Hermes.Wpf: type + sender + image_base64) ─

    public static function receive_message( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::table_exists() && class_exists( 'HIR_Activator', false ) ) {
            HIR_Activator::ensure_database();
        }

        $data   = $request->get_json_params();
        $type   = sanitize_text_field( $data['type'] ?? 'text' );
        $sender = sanitize_text_field( $data['sender'] ?? 'unknown' );

        if ( $type === 'text' ) {
            return new WP_REST_Response( [ 'success' => true, 'id' => 0 ], 200 );
        }

        if ( $type !== 'image' || empty( $data['image_base64'] ) ) {
            return new WP_REST_Response( [ 'error' => 'image_base64 required for type image' ], 400 );
        }

        $image_data = base64_decode( $data['image_base64'], true );
        if ( $image_data === false || strlen( $image_data ) < 10 ) {
            return new WP_REST_Response( [ 'error' => 'Invalid image data' ], 400 );
        }

        $mime     = self::guess_mime( $image_data );
        $filename = self::guess_filename( $image_data );
        $channel  = $sender !== '' ? $sender : 'default';
        $meta     = [ 'sender' => $sender, 'source' => 'hermes.wpf' ];

        $stored = self::store_image( $image_data, $channel, $filename, $mime, $meta, false );
        if ( is_wp_error( $stored ) ) {
            return new WP_REST_Response( [ 'error' => $stored->get_error_message() ], 500 );
        }

        return new WP_REST_Response(
            [
                'success' => true,
                'id'      => $stored['id'],
                'url'     => $stored['url'],
            ],
            200
        );
    }

    // ── Image: receive (legacy / token) ────────────────────────────────────────

    public static function receive_image( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::table_exists() && class_exists( 'HIR_Activator', false ) ) {
            HIR_Activator::ensure_database();
        }

        $body  = $request->get_json_params();
        $token = $body['token'] ?? $request->get_header( 'X-Hermes-Token' ) ?? '';

        if ( ! HIR_Helpers::verify_token( $token ) ) {
            return new WP_REST_Response( [ 'error' => 'Unauthorized' ], 401 );
        }

        $channel  = sanitize_text_field( $body['channel'] ?? 'default' );
        $filename = sanitize_file_name( $body['filename'] ?? 'image.png' );
        $mime     = sanitize_text_field( $body['mime'] ?? 'image/png' );
        $b64data  = $body['data'] ?? $body['image_base64'] ?? '';
        $meta     = is_array( $body['meta'] ?? null ) ? $body['meta'] : [];
        $sender   = sanitize_text_field( $body['sender'] ?? '' );
        if ( $sender !== '' ) {
            $meta['sender'] = $sender;
        }

        $image_data = base64_decode( $b64data, true );
        if ( $image_data === false || strlen( $image_data ) < 10 ) {
            return new WP_REST_Response( [ 'error' => 'Invalid image data' ], 400 );
        }

        $stored = self::store_image( $image_data, $channel, $filename, $mime, $meta, true );
        if ( is_wp_error( $stored ) ) {
            $code = $stored->get_error_code() === 'channel_not_allowed' ? 403 : 500;
            return new WP_REST_Response( [ 'error' => $stored->get_error_message() ], $code );
        }

        return new WP_REST_Response(
            [
                'success' => true,
                'id'      => $stored['id'],
                'url'     => $stored['url'],
                'channel' => $stored['channel'],
            ],
            201
        );
    }

    // ── Image: list ────────────────────────────────────────────────────────────

    public static function get_images( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::table_exists() ) {
            return new WP_REST_Response( [ 'images' => [] ], 200 );
        }

        global $wpdb;
        $table   = HIR_Helpers::table_name();
        $channel = sanitize_text_field( $request->get_param( 'channel' ) ?? '' );
        $since   = absint( $request->get_param( 'since' ) ?? 0 );
        $limit   = min( absint( $request->get_param( 'limit' ) ?? 20 ), 100 );

        $where  = [];
        $params = [];

        if ( $since > 0 ) {
            $where[]  = 'id > %d';
            $params[] = $since;
        }
        if ( $channel !== '' ) {
            $where[]  = 'channel = %s';
            $params[] = $channel;
        }

        $sql = "SELECT id, channel, filename, mime_type, file_path, meta, created_at FROM {$table}";
        if ( $where ) {
            $sql .= ' WHERE ' . implode( ' AND ', $where );
        }
        $sql .= ' ORDER BY id DESC LIMIT %d';
        $params[] = $limit;

        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.NotPrepared
        $prepared = call_user_func_array( [ $wpdb, 'prepare' ], array_merge( [ $sql ], $params ) );
        $rows     = $wpdb->get_results( $prepared );

        $images = [];
        foreach ( (array) $rows as $row ) {
            $url      = $row->file_path
                ? self::path_to_url( $row->file_path )
                : rest_url( "hermes/v1/image-data/{$row->id}" );
            $images[] = [
                'id'         => (int) $row->id,
                'channel'    => $row->channel,
                'filename'   => $row->filename,
                'mime_type'  => $row->mime_type,
                'url'        => $url,
                'meta'       => json_decode( $row->meta ?? '{}', true ),
                'created_at' => $row->created_at,
            ];
        }

        return new WP_REST_Response( [ 'images' => $images ], 200 );
    }

    // ── Image: serve raw binary ────────────────────────────────────────────────
    //
    // ИСПРАВЛЕНИЕ ПАДЕНИЯ САЙТА:
    // Прежний код делал echo + exit внутри REST callback — это обрывало
    // WordPress response pipeline и приводило к белому экрану / fatal error.
    // Правильный способ: перехватить отправку через фильтр rest_pre_serve_request.

    public static function serve_image_data( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::table_exists() ) {
            return new WP_REST_Response( [ 'error' => 'Not found' ], 404 );
        }

        global $wpdb;
        $id  = absint( $request['id'] );
        $row = $wpdb->get_row(
            $wpdb->prepare(
                'SELECT image_data, mime_type FROM ' . HIR_Helpers::table_name() . ' WHERE id = %d',
                $id
            )
        );

        if ( ! $row || ! $row->image_data ) {
            return new WP_REST_Response( [ 'error' => 'Not found' ], 404 );
        }

        $mime_type  = $row->mime_type;
        $image_data = $row->image_data;

        add_filter(
            'rest_pre_serve_request',
            static function ( $served ) use ( $mime_type, $image_data ) {
                if ( $served ) {
                    return $served;
                }
                header( 'Content-Type: ' . $mime_type );
                header( 'Content-Length: ' . strlen( $image_data ) );
                header( 'Cache-Control: max-age=86400, public' );
                // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped
                echo $image_data;
                return true;
            },
            10,
            1
        );

        return new WP_REST_Response( null, 200 );
    }

    // ── Image: delete all ──────────────────────────────────────────────────────

    public static function delete_images( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::table_exists() ) {
            return new WP_REST_Response( [ 'success' => true ], 200 );
        }

        global $wpdb;
        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.InterpolatedNotPrepared
        $wpdb->query( 'TRUNCATE TABLE ' . HIR_Helpers::table_name() );
        return new WP_REST_Response( [ 'success' => true ], 200 );
    }

    // ── SSE stream (browser ← WordPress) ───────────────────────────────────────
    //
    // Hermes.Wpf загружает изображение через POST /image (base64 JSON).
    // Браузер подключается EventSource к GET /stream и получает image_url.

    public static function stream() {
        header( 'Content-Type: text/event-stream' );
        header( 'Cache-Control: no-cache' );
        header( 'Connection: keep-alive' );
        header( 'X-Accel-Buffering: no' );

        @ini_set( 'output_buffering', 'off' );
        @ini_set( 'zlib.output_compression', false );
        while ( ob_get_level() ) {
            ob_end_flush();
        }

        if ( ! HIR_Helpers::table_exists() ) {
            echo ": no-table\n\n";
            flush();
            exit;
        }

        global $wpdb;
        $table   = HIR_Helpers::table_name();
        $last_id = isset( $_GET['last_id'] ) ? absint( $_GET['last_id'] ) : 0;
        $channel = sanitize_text_field( $_GET['channel'] ?? '' );

        ignore_user_abort( true );
        set_time_limit( 0 );

        while ( ! connection_aborted() ) {
            $where  = 'id > %d';
            $params = [ $last_id ];
            if ( $channel !== '' ) {
                $where   .= ' AND channel = %s';
                $params[] = $channel;
            }

            // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.InterpolatedNotPrepared
            $rows = $wpdb->get_results(
                $wpdb->prepare(
                    "SELECT id, channel, filename, mime_type, file_path, meta, created_at
                     FROM {$table} WHERE {$where} ORDER BY id ASC",
                    ...$params
                )
            );

            foreach ( (array) $rows as $row ) {
                $last_id = (int) $row->id;
                $url     = $row->file_path
                    ? self::path_to_url( $row->file_path )
                    : rest_url( "hermes/v1/image-data/{$row->id}" );

                $meta = json_decode( $row->meta ?? '{}', true );
                if ( ! is_array( $meta ) ) {
                    $meta = [];
                }

                echo 'id: ' . $row->id . "\n";
                echo 'data: ' . wp_json_encode(
                    [
                        'id'         => (int) $row->id,
                        'type'       => 'image',
                        'channel'    => $row->channel,
                        'sender'     => $meta['sender'] ?? '',
                        'filename'   => $row->filename,
                        'mime_type'  => $row->mime_type,
                        'image_url'  => $url,
                        'meta'       => $meta,
                        'created_at' => $row->created_at,
                    ]
                ) . "\n\n";
                flush();
            }

            echo ": ping\n\n";
            flush();
            sleep( 1 );
        }
        exit;
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    public static function get_status() {
        return new WP_REST_Response(
            [
                'status'       => 'ok',
                'version'      => HIR_VERSION,
                'port'         => (int) get_option( 'hir_ws_port', 8765 ),
                'sse'          => (bool) get_option( 'hir_use_sse', 1 ),
                'stream_url'   => rest_url( 'hermes/v1/stream' ),
                'table_exists' => HIR_Helpers::table_exists(),
            ],
            200
        );
    }

    // ── Logs: receive (POST /wp-json/hermes/v1/logs) ───────────────────────────
    //
    // Принимает пакет логов от Hermes.Wpf.
    // Авторизация: заголовок X-Hermes-Token или поле "token" в теле запроса.
    //
    // Тело запроса (application/json):
    // {
    //   "token":   "<secret>",
    //   "source":  "HermesWpf/1.0",        // необязательно
    //   "entries": [
    //     {
    //       "level":     "Info",            // Trace|Debug|Info|Warning|Error|Fatal
    //       "message":   "Текст лога",
    //       "timestamp": "2025-05-17T10:00:00.000Z",  // ISO 8601, UTC
    //       "category":  "ImageSender",     // необязательно
    //       "exception": "System.Exception...", // необязательно
    //       "extra":     { "key": "value" } // необязательно
    //     }
    //   ]
    // }

    public static function receive_logs( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::logs_table_exists() && class_exists( 'HIR_Activator', false ) ) {
            HIR_Activator::ensure_database();
        }

        $body  = $request->get_json_params();
        $token = $body['token'] ?? $request->get_header( 'X-Hermes-Token' ) ?? '';

        if ( ! HIR_Helpers::verify_token( $token ) ) {
            return new WP_REST_Response( [ 'error' => 'Unauthorized' ], 401 );
        }

        $entries = $body['entries'] ?? [];
        if ( ! is_array( $entries ) || empty( $entries ) ) {
            return new WP_REST_Response( [ 'error' => 'No log entries provided' ], 400 );
        }

        $source         = sanitize_text_field( $body['source'] ?? 'HermesWpf' );
        $allowed_levels = [ 'trace', 'debug', 'info', 'warning', 'error', 'fatal' ];
        $entries        = array_slice( $entries, 0, 200 ); // max 200 за раз

        global $wpdb;
        $table   = HIR_Helpers::logs_table_name();
        $saved   = 0;
        $skipped = 0;

        foreach ( $entries as $entry ) {
            if ( ! is_array( $entry ) ) {
                ++$skipped;
                continue;
            }

            $level   = strtolower( sanitize_text_field( $entry['level'] ?? 'info' ) );
            $message = sanitize_textarea_field( $entry['message'] ?? '' );

            if ( $message === '' ) {
                ++$skipped;
                continue;
            }

            if ( ! in_array( $level, $allowed_levels, true ) ) {
                $level = 'info';
            }

            $ts_raw    = $entry['timestamp'] ?? '';
            $ts_unix   = $ts_raw !== '' ? strtotime( $ts_raw ) : 0;
            $logged_at = $ts_unix > 0
                ? gmdate( 'Y-m-d H:i:s', $ts_unix )
                : current_time( 'mysql', true );

            $category  = sanitize_text_field( $entry['category'] ?? '' );
            $exception = sanitize_textarea_field( $entry['exception'] ?? '' );
            $extra     = isset( $entry['extra'] ) && is_array( $entry['extra'] )
                ? wp_json_encode( $entry['extra'] )
                : null;

            $result = $wpdb->insert(
                $table,
                [
                    'source'     => $source,
                    'level'      => $level,
                    'category'   => $category,
                    'message'    => $message,
                    'exception'  => $exception ?: null,
                    'extra'      => $extra,
                    'logged_at'  => $logged_at,
                    'created_at' => current_time( 'mysql', true ),
                ],
                [ '%s', '%s', '%s', '%s', '%s', '%s', '%s', '%s' ]
            );

            if ( false !== $result ) {
                ++$saved;
            } else {
                ++$skipped;
            }
        }

        // Ротация: оставляем только последние hir_max_logs записей.
        $max_logs = (int) get_option( 'hir_max_logs', 5000 );
        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.InterpolatedNotPrepared
        $wpdb->query(
            $wpdb->prepare(
                "DELETE FROM {$table} WHERE id NOT IN (
                    SELECT id FROM (SELECT id FROM {$table} ORDER BY id DESC LIMIT %d) tmp
                )",
                $max_logs
            )
        );

        return new WP_REST_Response(
            [
                'success' => true,
                'saved'   => $saved,
                'skipped' => $skipped,
            ],
            201
        );
    }

    // ── Logs: list (GET /wp-json/hermes/v1/logs) — только для администратора ──

    public static function get_logs( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::logs_table_exists() ) {
            return new WP_REST_Response( [ 'logs' => [] ], 200 );
        }

        global $wpdb;
        $table  = HIR_Helpers::logs_table_name();
        $level  = strtolower( sanitize_text_field( $request->get_param( 'level' ) ?? '' ) );
        $source = sanitize_text_field( $request->get_param( 'source' ) ?? '' );
        $since  = absint( $request->get_param( 'since' ) ?? 0 );
        $limit  = min( absint( $request->get_param( 'limit' ) ?? 50 ), 500 );

        $where  = [];
        $params = [];

        if ( $since > 0 ) {
            $where[]  = 'id > %d';
            $params[] = $since;
        }
        if ( $level !== '' ) {
            $where[]  = 'level = %s';
            $params[] = $level;
        }
        if ( $source !== '' ) {
            $where[]  = 'source = %s';
            $params[] = $source;
        }

        $sql = "SELECT id, source, level, category, message, exception, extra, logged_at FROM {$table}";
        if ( $where ) {
            $sql .= ' WHERE ' . implode( ' AND ', $where );
        }
        $sql .= ' ORDER BY id DESC LIMIT %d';
        $params[] = $limit;

        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.NotPrepared
        $prepared = call_user_func_array( [ $wpdb, 'prepare' ], array_merge( [ $sql ], $params ) );
        $rows     = $wpdb->get_results( $prepared );

        $logs = [];
        foreach ( (array) $rows as $row ) {
            $logs[] = [
                'id'        => (int) $row->id,
                'source'    => $row->source,
                'level'     => $row->level,
                'category'  => $row->category,
                'message'   => $row->message,
                'exception' => $row->exception,
                'extra'     => $row->extra ? json_decode( $row->extra, true ) : null,
                'logged_at' => $row->logged_at,
            ];
        }

        return new WP_REST_Response( [ 'logs' => $logs ], 200 );
    }

    // ── Logs: delete all ───────────────────────────────────────────────────────

    public static function delete_logs( WP_REST_Request $request ) {
        if ( ! HIR_Helpers::logs_table_exists() ) {
            return new WP_REST_Response( [ 'success' => true ], 200 );
        }

        global $wpdb;
        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.InterpolatedNotPrepared
        $wpdb->query( 'TRUNCATE TABLE ' . HIR_Helpers::logs_table_name() );
        return new WP_REST_Response( [ 'success' => true ], 200 );
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────

    public static function verify_admin() {
        return current_user_can( 'manage_options' );
    }

    /**
     * @param array<string,mixed> $meta
     * @return array{id:int,url:string,channel:string}|\WP_Error
     */
    private static function store_image( $image_data, $channel, $filename, $mime, $meta, $enforce_channel_allowlist ) {
        if ( $enforce_channel_allowlist ) {
            $allowed = get_option( 'hir_allowed_channels', '' );
            if ( ! empty( $allowed ) ) {
                $list = array_map( 'trim', explode( ',', $allowed ) );
                if ( ! in_array( $channel, $list, true ) ) {
                    return new WP_Error( 'channel_not_allowed', 'Channel not allowed' );
                }
            }
        }

        global $wpdb;
        $table     = HIR_Helpers::table_name();
        $file_path = null;

        if ( get_option( 'hir_save_to_disk', 1 ) ) {
            $file_path = self::save_image_to_disk( $image_data, $filename, $mime );
        }

        $inserted = $wpdb->insert(
            $table,
            [
                'channel'    => $channel,
                'filename'   => $filename,
                'mime_type'  => $mime,
                'image_data' => $file_path ? null : $image_data,
                'file_path'  => $file_path,
                'meta'       => wp_json_encode( $meta ),
                'created_at' => current_time( 'mysql' ),
            ],
            [ '%s', '%s', '%s', '%s', '%s', '%s', '%s' ]
        );

        if ( false === $inserted ) {
            return new WP_Error( 'db_error', 'Database error' );
        }

        $id = (int) $wpdb->insert_id;

        $max = (int) get_option( 'hir_max_images', 50 );
        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.InterpolatedNotPrepared
        $wpdb->query(
            $wpdb->prepare(
                "DELETE FROM {$table} WHERE id NOT IN (
                    SELECT id FROM (SELECT id FROM {$table} ORDER BY id DESC LIMIT %d) tmp
                )",
                $max
            )
        );

        $url = $file_path
            ? self::path_to_url( $file_path )
            : rest_url( "hermes/v1/image-data/{$id}" );

        return [
            'id'      => $id,
            'url'     => $url,
            'channel' => $channel,
        ];
    }

    private static function guess_mime( $data ) {
        if ( strlen( $data ) >= 3 && $data[0] === "\xFF" && $data[1] === "\xD8" && $data[2] === "\xFF" ) {
            return 'image/jpeg';
        }
        if ( strlen( $data ) >= 8 && substr( $data, 0, 8 ) === "\x89PNG\r\n\x1a\n" ) {
            return 'image/png';
        }
        if ( strlen( $data ) >= 6 && ( substr( $data, 0, 6 ) === 'GIF87a' || substr( $data, 0, 6 ) === 'GIF89a' ) ) {
            return 'image/gif';
        }
        return 'image/png';
    }

    private static function guess_filename( $data ) {
        $mime = self::guess_mime( $data );
        $ext  = self::mime_to_ext( $mime );
        return 'capture-' . gmdate( 'Ymd-His' ) . '.' . $ext;
    }

    private static function save_image_to_disk( $data, $filename, $mime ) {
        $upload = wp_upload_dir();
        if ( ! empty( $upload['error'] ) ) {
            return null;
        }

        $subdir = trim( (string) get_option( 'hir_upload_subdir', 'hermes-images' ), '/' );
        $dir    = $upload['basedir'] . '/' . $subdir . '/' . gmdate( 'Y/m' );

        wp_mkdir_p( $dir );

        $ext  = self::mime_to_ext( $mime );
        $base = pathinfo( $filename, PATHINFO_FILENAME );
        $path = $dir . '/' . $base . '-' . uniqid( '', true ) . '.' . $ext;

        if ( false === file_put_contents( $path, $data ) ) {
            return null;
        }

        return $path;
    }

    private static function path_to_url( $path ) {
        $upload = wp_upload_dir();
        return str_replace( $upload['basedir'], $upload['baseurl'], $path );
    }

    private static function mime_to_ext( $mime ) {
        $map = [
            'image/png'  => 'png',
            'image/jpeg' => 'jpg',
            'image/webp' => 'webp',
            'image/gif'  => 'gif',
            'image/bmp'  => 'bmp',
        ];
        return $map[ $mime ] ?? 'png';
    }
}
