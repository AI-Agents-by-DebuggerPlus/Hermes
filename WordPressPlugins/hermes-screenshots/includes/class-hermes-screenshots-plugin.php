<?php
declare(strict_types=1);

if (!defined('ABSPATH')) {
    exit;
}

final class Hermes_Screenshots_Plugin
{
    private const OPTION_POLL = 'hermes_screenshots_poll_seconds';

    private static ?self $instance = null;

    public static function instance(): self
    {
        if (self::$instance === null) {
            self::$instance = new self();
        }

        return self::$instance;
    }

    private function __construct()
    {
        add_action('rest_api_init', [Hermes_Screenshots_Rest_Controller::class, 'register']);
        add_action('admin_menu', [$this, 'register_settings_page']);
        add_action('admin_init', [$this, 'register_settings']);
        add_action('wp_enqueue_scripts', [$this, 'enqueue_assets']);
        add_shortcode('hermes_screenshot', [$this, 'render_shortcode']);
    }

    public function register_settings_page(): void
    {
        add_options_page(
            __('Hermes Screenshots', 'hermes-screenshots'),
            __('Hermes Screenshots', 'hermes-screenshots'),
            'manage_options',
            'hermes-screenshots',
            [$this, 'render_settings_page']
        );
    }

    public function register_settings(): void
    {
        register_setting('hermes_screenshots', Hermes_Screenshots_Rest_Controller::OPTION_API_KEY, [
            'type' => 'string',
            'sanitize_callback' => 'sanitize_text_field',
            'default' => '',
        ]);
        register_setting('hermes_screenshots', self::OPTION_POLL, [
            'type' => 'integer',
            'sanitize_callback' => static function ($value): int {
                $n = (int) $value;
                return max(3, min(120, $n > 0 ? $n : 10));
            },
            'default' => 10,
        ]);
    }

    public function enqueue_assets(): void
    {
        if (!is_singular() && !is_front_page()) {
            return;
        }

        global $post;
        if (!$post instanceof WP_Post) {
            return;
        }

        if (!has_shortcode((string) $post->post_content, 'hermes_screenshot')) {
            return;
        }

        wp_enqueue_style(
            'hermes-screenshots',
            plugins_url('assets/css/viewer.css', HERMES_SCREENSHOTS_PLUGIN_FILE),
            [],
            HERMES_SCREENSHOTS_VERSION
        );

        wp_enqueue_script(
            'hermes-screenshots',
            plugins_url('assets/js/viewer.js', HERMES_SCREENSHOTS_PLUGIN_FILE),
            [],
            HERMES_SCREENSHOTS_VERSION,
            true
        );

        wp_localize_script('hermes-screenshots', 'HermesScreenshots', [
            'ajaxUrl' => admin_url('admin-ajax.php'),
            'nonce' => wp_create_nonce('hermes_screenshots_poll'),
            'pollSeconds' => $this->poll_seconds(),
        ]);
    }

    public function render_shortcode(): string
    {
        $payload = Hermes_Screenshots_Rest_Controller::get_latest_meta();
        if ($payload === null) {
            return '<div class="hermes-screenshot hermes-screenshot--empty">'
                . esc_html__('Скриншот Hermes ещё не загружен.', 'hermes-screenshots')
                . '</div>';
        }

        return $this->render_viewer_html($payload);
    }

    public function render_settings_page(): void
    {
        if (!current_user_can('manage_options')) {
            return;
        }

        if (isset($_POST['hermes_regenerate_key']) && check_admin_referer('hermes_screenshots_regen')) {
            update_option(Hermes_Screenshots_Rest_Controller::OPTION_API_KEY, Hermes_Screenshots_Rest_Controller::generate_api_key(), false);
            echo '<div class="notice notice-success"><p>' . esc_html__('API key regenerated.', 'hermes-screenshots') . '</p></div>';
        }

        $api_key = (string) get_option(Hermes_Screenshots_Rest_Controller::OPTION_API_KEY, '');
        if ($api_key === '') {
            $api_key = Hermes_Screenshots_Rest_Controller::generate_api_key();
            update_option(Hermes_Screenshots_Rest_Controller::OPTION_API_KEY, $api_key, false);
        }

        $endpoint = esc_url(rest_url(Hermes_Screenshots_Rest_Controller::NAMESPACE . Hermes_Screenshots_Rest_Controller::ROUTE));

        ?>
        <div class="wrap">
            <h1><?php esc_html_e('Hermes Screenshots', 'hermes-screenshots'); ?></h1>
            <p><?php esc_html_e('Прямая загрузка скриншота с Hermes.Wpf (без Supabase).', 'hermes-screenshots'); ?></p>
            <p><code>[hermes_screenshot]</code></p>

            <h2><?php esc_html_e('Hermes.Wpf', 'hermes-screenshots'); ?></h2>
            <table class="form-table" role="presentation">
                <tr>
                    <th><?php esc_html_e('Site URL', 'hermes-screenshots'); ?></th>
                    <td><code><?php echo esc_html(home_url()); ?></code></td>
                </tr>
                <tr>
                    <th><?php esc_html_e('REST endpoint', 'hermes-screenshots'); ?></th>
                    <td><code><?php echo $endpoint; ?></code> (POST, multipart <code>file</code>)</td>
                </tr>
                <tr>
                    <th><?php esc_html_e('API key', 'hermes-screenshots'); ?></th>
                    <td>
                        <input type="text" class="large-text" readonly value="<?php echo esc_attr($api_key); ?>" onclick="this.select();" />
                        <form method="post" style="margin-top:8px;">
                            <?php wp_nonce_field('hermes_screenshots_regen'); ?>
                            <button type="submit" name="hermes_regenerate_key" class="button"><?php esc_html_e('Regenerate key', 'hermes-screenshots'); ?></button>
                        </form>
                    </td>
                </tr>
            </table>

            <form method="post" action="options.php">
                <?php settings_fields('hermes_screenshots'); ?>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><label for="hermes_poll"><?php esc_html_e('Poll interval (sec)', 'hermes-screenshots'); ?></label></th>
                        <td><input name="<?php echo esc_attr(self::OPTION_POLL); ?>" id="hermes_poll" type="number" min="3" max="120" value="<?php echo esc_attr((string) $this->poll_seconds()); ?>" /></td>
                    </tr>
                </table>
                <?php submit_button(); ?>
            </form>
        </div>
        <?php
    }

    /**
     * @param array<string, mixed> $payload
     */
    private function render_viewer_html(array $payload): string
    {
        $image_url = esc_url((string) $payload['image_url']);
        $captured = isset($payload['captured_at']) ? esc_html((string) $payload['captured_at']) : '';
        $fg = isset($payload['foreground']) ? esc_html((string) $payload['foreground']) : '';
        $size = '';
        if (!empty($payload['width']) && !empty($payload['height'])) {
            $size = esc_html((string) $payload['width'] . '×' . (string) $payload['height']);
        }

        $meta = trim($captured . ($size !== '' ? ' · ' . $size : '') . ($fg !== '' ? ' · ' . $fg : ''));

        ob_start();
        ?>
        <div class="hermes-screenshot" data-hermes-screenshot="1">
            <?php if ($meta !== '') : ?>
                <p class="hermes-screenshot__meta"><?php echo $meta; ?></p>
            <?php endif; ?>
            <a class="hermes-screenshot__link" href="<?php echo esc_url(preg_replace('/\?ver=\d+$/', '', $image_url)); ?>" target="_blank" rel="noopener">
                <img class="hermes-screenshot__img" src="<?php echo $image_url; ?>" alt="<?php esc_attr_e('Hermes screenshot', 'hermes-screenshots'); ?>" loading="lazy" />
            </a>
        </div>
        <?php
        return (string) ob_get_clean();
    }

    private function poll_seconds(): int
    {
        return (int) get_option(self::OPTION_POLL, 10);
    }
}

add_action('wp_ajax_hermes_screenshots_latest', static function (): void {
    check_ajax_referer('hermes_screenshots_poll', 'nonce');
    wp_send_json_success(['html' => Hermes_Screenshots_Plugin::instance()->render_shortcode()]);
});
add_action('wp_ajax_nopriv_hermes_screenshots_latest', static function (): void {
    check_ajax_referer('hermes_screenshots_poll', 'nonce');
    wp_send_json_success(['html' => Hermes_Screenshots_Plugin::instance()->render_shortcode()]);
});
