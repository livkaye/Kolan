import { LitElement, html, css, property, customElement } from "lit-element";
import { IDialogTemplate } from "../dialogs/IDialogTemplate";

/**
 * Dialog element that takes an IDialogTemplate as input
 * and returns an object with the values as an event.
 */
@customElement('dialog-box')
export class DialogBox extends LitElement {
   @property({type: Boolean}) shown = false;
   dialogOptions: IDialogTemplate;

   constructor(dialogOptions, id) {
      super();
      this.dialogOptions = dialogOptions;
      this.id = id;
   }

   render() {
      return html`
        <link rel="stylesheet" type="text/css" href="../css/components/dialog.css">
         <div class="dialogBackground"></div>
         <section class="dialog">
            <h2>${html`${this.dialogOptions.title}`}</h2>
            ${this.dialogOptions.inputs.map(x => html`<p>${x.value}:</p>
               <input type="text" placeholder="${x.value}..." /><br />`)}
            <button @click="${this.submitHandler}">${html`${this.dialogOptions.primaryButton}`}</button>
            <button class="secondary" @click="${this.cancelHandler}">Cancel</button>
         </section>`;
   }

   updated() {
      this.style.display = this.shown ? "block" : "none";
   }

   /**
    * When the submit button in the dialog is clicked. Fires the event, hides
    * and clears the dialog
    */
   submitHandler() {
      // Fire event
      this.dispatchEvent(new CustomEvent("submitDialog", {
         detail: this.getInputValues()
      }));

      this.hide();
   }

   /** When the cancel button in the dialog is clicked. Hides and clears 
    * the dialog
    */
   cancelHandler() {
      this.hide();
   }

   /** Get user input in the dialog
    */
   private getInputValues() {
      let input = {};
      let dialogItems = <any>this.shadowRoot.querySelector(".dialog").children;
      let counter = 0; // Index for each "input" element looped through
      for (let element of dialogItems) {
         if (element.tagName == "INPUT") {
            let key = this.dialogOptions.inputs[counter].key; // Gets the prefered key value for the input element
            input[key] = element.value;
            counter++;
         }
      }

      return input;
   }

   /** Hide the dialog and clear the values
    */
   private hide() {
      let dialogItems = <any>this.shadowRoot.querySelector(".dialog").children;
      for (let element of dialogItems) {
         if (element.tagName == "INPUT")
            element.value = "";
      }

      this.shown = false;
   }
}
