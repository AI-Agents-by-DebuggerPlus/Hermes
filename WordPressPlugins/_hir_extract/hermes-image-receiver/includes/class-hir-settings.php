<?php
if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

if ( class_exists( 'HIR_Settings', false ) ) {
    return;
}

class HIR_Settings {

    public static function init() {
        add_action( 'admin_menu',            [ __CLASS__, 'add_menu'            ] );
        add_action( 'admin_init',            [ __CLASS__, 'register_settings'   ] );
        add_action( 'admin_enqueue_scripts', [ __CLASS__, 'enqueue_admin_assets'] );
    }

    public static function add_menu() {
        add_options_page(
            'Hermes Image Receiver',
            'Hermes Receiver',
            'manage_options',
            'hermes-image-receiver',
            [ __CLASS__, 'render_page' ]
        );
    }

    public static function enqueue_admin_assets( $hook ) {
        if ( $hook !== 'settings_page_hermes-image-receiver' ) {
            return;
        }
        wp_enqueue_style(
            'hir-admin',
            HIR_PLUGIN_URL . 'assets/css/admin.css',
            [],
            HIR_VERSION
        );
        wp_enqueue_script(
            'hir-admin',
            HIR_PLUGIN_URL . 'assets/js/admin.js',
            [ 'jquery' ],
            HIR_VERSION,
            true
        );
        wp_localize_script( 'hir-admin', 'hirAdmin', [
            'ajaxUrl' => admin_url( 'admin-ajax.php' ),
            'nonce'   => wp_create_nonce( 'hir_admin_nonce' ),
            'restUrl' => rest_url( 'hermes/v1/' ),
            'token'   => get_option( 'hir_secret_token' ),
        ] );
    }

    public static function register_settings() {
        $fields = [
            'hir_max_images'       => [ 'sanitize_callback' => 'absint'              ],
            'hir_save_to_disk'     => [ 'sanitize_callback' => 'absint'              ],
            'hir_upload_subdir'    => [ 'sanitize_callback' => 'sanitize_file_name'  ],
            'hir_allowed_channels' => [ 'sanitize_callback' => 'sanitize_text_field' ],
            'hir_secret_token'     => [ 'sanitize_callback' => 'sanitize_text_field' ],
            'hir_use_sse'          => [ 'sanitize_callback' => 'absint'              ],
        ];
        foreach ( $fields as $key => $args ) {
            register_setting( 'hir_settings_group', $key, $args );
        }
    }

    public static function render_page() {
        if ( ! current_user_can( 'manage_options' ) ) {
            return;
        }

        $token    = get_option( 'hir_secret_token' );
        $rest_msg = rest_url( 'hermes/v1/message' );
        $sse_url  = rest_url( 'hermes/v1/stream' );
        ?>
        <div class="wrap hir-admin-wrap">
            <h1>Hermes Image Receiver</h1>
            <p class="description">
                Hermes.Wpf → <code>POST /hermes/v1/message</code> (type, sender, image_base64). Галерея ← SSE.
            </p>

            <div class="hir-status-bar">
                <span class="hir-status-dot" id="hir-status-dot"></span>
                <span id="hir-status-text">Проверка сервера…</span>
                <button type="button" class="button button-secondary" id="hir-refresh-status">↻ Обновить</button>
            </div>

            <div class="hir-grid">
                <div class="hir-card">
                    <h2>Подключение</h2>
                    <form method="post" action="options.php">
                        <?php settings_fields( 'hir_settings_group' ); ?>
                        <input type="hidden" name="hir_use_sse" value="1" />
                        <table class="form-table">
                            <tr>
                                <th>Секретный токен</th>
                                <td>
                                    <input type="text" name="hir_secret_token"
                                        value="<?php echo esc_attr( $token ); ?>"
                                        class="large-text code" id="hir-token-field" readonly />
                                    <button type="button" class="button" id="hir-regen-token">Сгенерировать новый</button>
                                    <p class="description">Только для legacy <code>/hermes/v1/image</code>. Hermes.Wpf через <code>/message</code> токен не требует.</p>
                                </td>
                            </tr>
                            <tr>
                                <th>Разрешённые каналы</th>
                                <td>
                                    <input type="text" name="hir_allowed_channels"
                                        value="<?php echo esc_attr( get_option( 'hir_allowed_channels', '' ) ); ?>"
                                        class="regular-text" placeholder="camera1, camera2" />
                                    <p class="description">Только для <code>/image</code>. Для <code>/message</code> канал = поле sender из Hermes.Wpf (фильтр «Разрешённые каналы» не применяется).</p>
                                </td>
                            </tr>
                            <tr>
                                <th>Макс. изображений</th>
                                <td>
                                    <input type="number" name="hir_max_images"
                                        value="<?php echo esc_attr( get_option( 'hir_max_images', 50 ) ); ?>"
                                        min="1" max="1000" class="small-text" />
                                </td>
                            </tr>
                            <tr>
                                <th>Сохранять в uploads</th>
                                <td>
                                    <label>
                                        <input type="checkbox" name="hir_save_to_disk" value="1"
                                            <?php checked( 1, get_option( 'hir_save_to_disk', 1 ) ); ?> />
                                        Сохранять файлы на диск
                                    </label>
                                </td>
                            </tr>
                            <tr>
                                <th>Подпапка uploads</th>
                                <td>
                                    <input type="text" name="hir_upload_subdir"
                                        value="<?php echo esc_attr( get_option( 'hir_upload_subdir', 'hermes-images' ) ); ?>"
                                        class="regular-text" />
                                </td>
                            </tr>
                        </table>
                        <?php submit_button( 'Сохранить' ); ?>
                    </form>
                </div>

                <div class="hir-card">
                    <h2>Hermes.Wpf и галерея</h2>
                    <div class="hir-code-block">
                        <strong>REST (Hermes.Wpf):</strong><br>
                        <code><?php echo esc_html( $rest_msg ); ?></code>
                    </div>
                    <div class="hir-code-block">
                        <strong>SSE (live в браузере):</strong><br>
                        <code><?php echo esc_html( $sse_url ); ?></code>
                        <p class="description" style="margin-top:8px;">
                            Параметры: <code>?channel=SENDER&amp;last_id=0</code> (sender из Hermes.Wpf)
                        </p>
                    </div>
                    <div class="hir-code-block">
                        <strong>Токен:</strong><br>
                        <code><?php echo esc_html( $token ); ?></code>
                    </div>
                    <h3>Шорткод</h3>
                    <div class="hir-code-block">
                        <code>[hermes_gallery channel="ИМЯ-ОТПРАВИТЕЛЯ"]</code>
                    </div>
                </div>
            </div>

            <div class="hir-card hir-full">
                <h2>Последние изображения</h2>
                <div id="hir-recent-images"><p>Загрузка…</p></div>
                <button type="button" class="button button-danger" id="hir-clear-all">🗑 Очистить всё</button>
            </div>
        </div>
        <?php
    }
}
