<?php
if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

if ( class_exists( 'HIR_Shortcode', false ) ) {
    return;
}

class HIR_Shortcode {

    public static function init() {
        add_shortcode( 'hermes_gallery', [ __CLASS__, 'render' ] );
        add_action( 'wp_enqueue_scripts', [ __CLASS__, 'register_assets' ] );
    }

    public static function register_assets() {
        wp_register_style(
            'hir-gallery',
            HIR_PLUGIN_URL . 'assets/css/gallery.css',
            [],
            HIR_VERSION
        );
        wp_register_script(
            'hir-gallery',
            HIR_PLUGIN_URL . 'assets/js/gallery.js',
            [],
            HIR_VERSION,
            true
        );
    }

    public static function render( $atts ) {
        $atts = shortcode_atts(
            [
                'channel'     => '',
                'max'         => 20,
                'autoconnect' => 'true',
                'ws_port'     => get_option( 'hir_ws_port', 8765 ),
                'ws_host'     => '',
                'layout'      => 'grid',
                'title'       => 'Hermes Live Feed',
            ],
            $atts,
            'hermes_gallery'
        );

        $ws_client = trim( (string) get_option( 'hir_ws_client_host', '' ) );
        if ( $atts['ws_host'] === '' && $ws_client !== '' ) {
            $atts['ws_host'] = $ws_client;
        }

        wp_enqueue_style( 'hir-gallery' );
        wp_enqueue_script( 'hir-gallery' );

        $uid = 'hir-' . wp_generate_password( 8, false, false );

        $use_sse = (bool) get_option( 'hir_use_sse', 1 );

        $config = [
            'uid'         => $uid,
            'channel'     => sanitize_text_field( $atts['channel'] ),
            'max'         => absint( $atts['max'] ),
            'autoconnect' => ( $atts['autoconnect'] === 'true' ),
            'restUrl'     => rest_url( 'hermes/v1/' ),
            'sseUrl'      => rest_url( 'hermes/v1/stream' ),
            'useSse'      => $use_sse,
            'token'       => (string) get_option( 'hir_secret_token', '' ),
            'layout'      => sanitize_text_field( $atts['layout'] ),
        ];

        $config_json = wp_json_encode( $config );
        if ( false === $config_json ) {
            $config_json = '{}';
        }

        ob_start();
        ?>
        <div class="hir-gallery-wrap hir-layout-<?php echo esc_attr( $atts['layout'] ); ?>"
             id="<?php echo esc_attr( $uid ); ?>"
             data-uid="<?php echo esc_attr( $uid ); ?>"
             data-hir-config="<?php echo esc_attr( $config_json ); ?>"
             data-channel="<?php echo esc_attr( $atts['channel'] ); ?>"
             data-max="<?php echo esc_attr( $atts['max'] ); ?>"
             data-autoconnect="<?php echo esc_attr( $atts['autoconnect'] ); ?>"
             data-ws-port="<?php echo esc_attr( $atts['ws_port'] ); ?>"
             data-layout="<?php echo esc_attr( $atts['layout'] ); ?>">

            <div class="hir-header">
                <div class="hir-title">
                    <span class="hir-led" data-uid="<?php echo esc_attr( $uid ); ?>"></span>
                    <?php echo esc_html( $atts['title'] ); ?>
                    <?php if ( $atts['channel'] ) : ?>
                        <span class="hir-channel-badge"><?php echo esc_html( $atts['channel'] ); ?></span>
                    <?php endif; ?>
                </div>
                <div class="hir-controls">
                    <span class="hir-counter" data-uid="<?php echo esc_attr( $uid ); ?>">0 фото</span>
                    <button type="button" class="hir-btn hir-btn-connect" data-uid="<?php echo esc_attr( $uid ); ?>">
                        Подключить
                    </button>
                    <button type="button" class="hir-btn hir-btn-clear" data-uid="<?php echo esc_attr( $uid ); ?>">
                        Очистить
                    </button>
                    <button type="button" class="hir-btn hir-btn-fslive" data-uid="<?php echo esc_attr( $uid ); ?>"
                            title="Включить: каждое новое изображение будет открываться на весь экран">
                        ▶ Авто-показ
                    </button>
                    <button type="button" class="hir-btn hir-btn-fullscreen" data-uid="<?php echo esc_attr( $uid ); ?>"
                            title="Развернуть на весь экран">
                        ⛶ На весь экран
                    </button>
                </div>
            </div>

            <div class="hir-status-bar">
                <span class="hir-status-msg" data-uid="<?php echo esc_attr( $uid ); ?>">
                    Нажмите «Подключить» для начала трансляции
                </span>
            </div>

            <div class="hir-images" data-uid="<?php echo esc_attr( $uid ); ?>">
                <div class="hir-empty-state">
                    <div class="hir-empty-icon">&#128225;</div>
                    <p>Ожидание изображений от Hermes.Wpf…</p>
                </div>
            </div>

            <div class="hir-lightbox" data-uid="<?php echo esc_attr( $uid ); ?>" style="display:none;">
                <div class="hir-lightbox-overlay"></div>
                <div class="hir-lightbox-content">
                    <button type="button" class="hir-lightbox-close">&#10005;</button>
                    <button type="button" class="hir-lightbox-prev">&#8249;</button>
                    <button type="button" class="hir-lightbox-next">&#8250;</button>
                    <img class="hir-lightbox-img" src="" alt="" />
                    <div class="hir-lightbox-meta"></div>
                </div>
            </div>

            <!-- Fullscreen-live overlay: показывается при каждом новом изображении -->
            <div class="hir-fs-overlay" data-uid="<?php echo esc_attr( $uid ); ?>">
                <div class="hir-fs-overlay-progress"></div>
                <img class="hir-fs-overlay-img" src="" alt="new image" />
                <div class="hir-fs-overlay-bar">
                    <span class="hir-fs-overlay-meta"></span>
                    <button type="button" class="hir-fs-overlay-close">✕ Закрыть</button>
                </div>
            </div>

        </div>
        <?php
        return ob_get_clean();
    }
}
