import alert, { SweetAlertOptions } from 'sweetalert2'

export class Modal {
  private static cssClasses = {
    container: '',
    popup: '',
    header: '',
    title: '',
    closeButton: '',
    icon: '',
    image: '',
    content: '',
    input: '',
    validationMessage: '',
    actions: '',
    confirmButton: 'button is-primary',
    denyButton: '',
    cancelButton: '',
    loader: '',
    footer: '',
  }

  private static defaultMessage: SweetAlertOptions = {
    buttonsStyling: false,
    showCloseButton: true,
    showClass: {
      popup: 'swal2-no-animation',
    },
    customClass: {
      ...Modal.cssClasses
    },
  }

  public static async showError(title: string, body: string): Promise<void> {
    await alert.fire({
      ...this.defaultMessage,
      title: title,
      text: body,
      icon: 'error',
    })
  }

  public static async showNetworkAnalysis(networkAnalysis: string): Promise<void> {
    await alert.fire({
      ...this.defaultMessage,
      title: 'Network analysis',
      html: `<div class="alert-modal-text preformatted">${networkAnalysis}</div> `,
      icon: 'info',
      width: 'auto',
    })
  }
}
