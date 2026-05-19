<?php
declare(strict_types=1);

if (!defined('ABSPATH')) {
    exit;
}

final class Hermes_Screenshots_Rest_Controller
{
    public const NAMESPACE = 'hermes/v1';
    public const ROUTE = '/screenshot';
    public const OPTION_API_KEY = 'hermes_screenshots_api_key';
    public const OPTION_LATEST_META = 'hermes_screenshots_latest_meta';

    public static function register(): void
    {
        register_rest_route(
            self::NAMESPACE,
            self::ROUTE,
            [
                'methods' => WP_REST_Server::CREATABLE,
                'callback' => [self::class, 'handle_upload'],
                'permission_callback' => [self::class, 'check_api_key'],
            ]
        );
    }

    /**
     * @param WP_REST_Request $request
     * @return WP_REST_Response|WP_Error
     */
    public static function handle_upload(WP_REST_Request $request)
    {
        $files = $request->get_file_params();
        if (empty($files['file']['tmp_name']) || !is_uploaded_file($files['file']['tmp_name'])) {
            return new WP_Error('hermes_no_file', 'Missing file field "file".', ['status' => 400]);
        }

        $upload_dir = self::ensure_upload_dir();
        if ($upload_dir === null) {
            return new WP_Error('hermes_upload_dir', 'Cannot create upload directory.', ['status' => 500]);
        }

        $latest_path = $upload_dir['path'] . '/latest.png';
        if (!move_uploaded_file($files['file']['tmp_name'], $latest_path)) {
            return new WP_Error('hermes_save_failed', 'Failed to save screenshot.', ['status' => 500]);
        }

        $captured_at = sanitize_text_field((string) $request->get_param('captured_at'));
        if ($captured_at === '') {
            $captured_at = gmdate('c');
        }

        $foreground = sanitize_text_field((string) $request->get_param('foreground'));
        $width = (int) $request->get_param('width');
        $height = (int) $request->get_param('height');

        $meta = [
            'image_url' => $upload_dir['url'] . '/latest.png',
            'captured_at' => $captured_at,
            'foreground' => $foreground,
            'width' => $width,
            'height' => $height,
            'updated_unix' => time(),
        ];

        update_option(self::OPTION_LATEST_META, $meta, false);

        return new WP_REST_Response(
            [
                'success' => true,
                'image_url' => $meta['image_url'],
                'captured_at' => $captured_at,
            ],
            200
        );
    }

  /**
     * @return array{path: string, url: string}|null
     */
    public static function ensure_upload_dir(): ?array
    {
        $wp_upload = wp_upload_dir();
        if (!empty($wp_upload['error'])) {
            return null;
        }

        $subdir = '/hermes-screenshots';
        $path = $wp_upload['basedir'] . $subdir;
        $url = $wp_upload['baseurl'] . $subdir;

        if (!wp_mkdir_p($path)) {
            return null;
        }

        // Deny PHP execution in upload folder.
        $htaccess = $path . '/.htaccess';
        if (!file_exists($htaccess)) {
            file_put_contents($htaccess, "Options -Indexes\n<Files *.php>\ndeny from all\n</Files>\n");
        }

        return ['path' => $path, 'url' => $url];
    }

    public static function check_api_key(WP_REST_Request $request): bool
    {
        $expected = (string) get_option(self::OPTION_API_KEY, '');
        if ($expected === '') {
            return false;
        }

        $provided = (string) $request->get_header('x-hermes-api-key');
        if ($provided === '') {
            $provided = (string) $request->get_param('api_key');
        }

        return $provided !== '' && hash_equals($expected, $provided);
    }

    /** @return array<string, mixed>|null */
    public static function get_latest_meta(): ?array
    {
        $meta = get_option(self::OPTION_LATEST_META, null);
        if (!is_array($meta) || empty($meta['image_url'])) {
            return null;
        }

        $path = self::ensure_upload_dir();
        if ($path !== null) {
            $file = $path['path'] . '/latest.png';
            if (!is_readable($file)) {
                return null;
            }

            $meta['image_url'] = $path['url'] . '/latest.png?ver=' . (int) ($meta['updated_unix'] ?? time());
        }

        return $meta;
    }

    public static function generate_api_key(): string
    {
        return wp_generate_password(32, false, false);
    }
}
